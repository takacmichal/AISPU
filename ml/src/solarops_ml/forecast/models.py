"""Trainable models and the two no-training baselines for spike #191.

LightGBM, scikit-learn and PyTorch are optional experiment dependencies. They are
imported lazily so the production ML image (``uv sync`` without the ``experiment``
group) does not need torch or LightGBM.
"""

from __future__ import annotations

import copy
import io
import os
from dataclasses import dataclass
from time import perf_counter

import numpy as np
import pandas as pd

from solarops_ml.forecast.data import FEATURES

# See experiment.py: LightGBM and PyTorch bundle separate OpenMP runtimes.
os.environ.setdefault("OMP_NUM_THREADS", "1")

MODEL_NAMES = ("persistence", "physical", "linear", "lightgbm", "lstm")
TARGET = "y"
VALIDATION_FRACTION = 0.15
VALIDATION_GAP_DAYS = 7


@dataclass(frozen=True)
class ModelPrediction:
    """Predictions aligned with the test frame index plus cost metadata."""

    y_pred: np.ndarray
    train_seconds: float
    inference_seconds: float
    model_bytes: int


def fit_predict(
    model: str,
    train: pd.DataFrame,
    test: pd.DataFrame,
    history: pd.DataFrame,
    *,
    seed: int = 42,
    lstm_window: int = 24,
    lstm_hidden: int = 32,
    lstm_epochs: int = 20,
) -> ModelPrediction:
    """Fit ``model`` on ``train`` and predict every row of ``test``.

    ``history`` contains all rows before the test period; the LSTM uses it to build
    the 24-hour input window without looking at any future feature value.
    """
    if model == "persistence":
        start = perf_counter()
        prediction = test["y_lag_24h"].to_numpy(dtype=float)
        return ModelPrediction(prediction, 0.0, _per_1000(perf_counter() - start, len(test)), 0)
    if model == "physical":
        start = perf_counter()
        prediction = test["p_baseline"].to_numpy(dtype=float)
        return ModelPrediction(prediction, 0.0, _per_1000(perf_counter() - start, len(test)), 0)
    if model == "linear":
        return _fit_linear(train, test)
    if model == "lightgbm":
        return _fit_lightgbm(train, test, seed=seed)
    if model == "lstm":
        return _fit_lstm(
            train,
            test,
            history,
            seed=seed,
            window=lstm_window,
            hidden=lstm_hidden,
            epochs=lstm_epochs,
        )
    raise ValueError(f"unknown model {model!r}; expected one of {MODEL_NAMES}")


def _training_mask(frame: pd.DataFrame) -> pd.Series:
    return (
        frame["is_day"].fillna(False) & frame[TARGET].notna() & frame[FEATURES].notna().all(axis=1)
    )


def _time_split(frame: pd.DataFrame) -> tuple[pd.Series, pd.Series]:
    """Last 15 % of the training period as validation, with a 7-day gap."""
    index = frame.index
    start, end = index.min(), index.max()
    cut = start + (end - start) * (1 - VALIDATION_FRACTION)
    val_start = cut + pd.Timedelta(days=VALIDATION_GAP_DAYS)
    train_mask = index < cut
    val_mask = index >= val_start
    if val_mask.sum() < 50:
        cut = start + (end - start) * 0.9
        val_start = cut
        train_mask = index < cut
        val_mask = index >= val_start
    return pd.Series(train_mask, index=index), pd.Series(val_mask, index=index)


def _fit_linear(train: pd.DataFrame, test: pd.DataFrame) -> ModelPrediction:
    from sklearn.linear_model import LinearRegression

    start = perf_counter()
    mask = _training_mask(train)
    x_train = train.loc[mask, FEATURES].to_numpy(dtype=float)
    y_train = train.loc[mask, TARGET].to_numpy(dtype=float)
    model = LinearRegression().fit(x_train, y_train)
    train_seconds = perf_counter() - start

    start = perf_counter()
    prediction = np.full(len(test), np.nan, dtype=float)
    valid = test[FEATURES].notna().all(axis=1).to_numpy()
    if valid.any():
        prediction[valid] = model.predict(test.loc[valid, FEATURES].to_numpy(dtype=float))
    inference_seconds = _per_1000(perf_counter() - start, len(test))
    model_bytes = int(model.coef_.nbytes + model.intercept_.nbytes)
    return ModelPrediction(prediction, train_seconds, inference_seconds, model_bytes)


def _fit_lightgbm(train: pd.DataFrame, test: pd.DataFrame, *, seed: int) -> ModelPrediction:
    import lightgbm as lgb

    start = perf_counter()
    mask = _training_mask(train)
    train_rows = train.loc[mask]
    x_train = train_rows[FEATURES].to_numpy(dtype=float)
    y_train = train_rows[TARGET].to_numpy(dtype=float)

    train_part, val_part = _time_split(train_rows)
    train_part = train_part.to_numpy()
    val_part = val_part.to_numpy()
    dtrain = lgb.Dataset(
        x_train[train_part],
        label=y_train[train_part],
        feature_name=FEATURES,
    )
    params = {
        "objective": "regression",
        "learning_rate": 0.03,
        "num_leaves": 31,
        "min_child_samples": 100,
        "feature_fraction": 0.8,
        "bagging_fraction": 0.8,
        "bagging_freq": 1,
        "seed": seed,
        "deterministic": True,
        "force_col_wise": True,
        "num_threads": 1,
        "verbosity": -1,
    }
    callbacks = [lgb.log_evaluation(0)]
    valid_sets = []
    if val_part.sum() >= 50:
        valid_sets = [
            lgb.Dataset(
                x_train[val_part],
                label=y_train[val_part],
                reference=dtrain,
                feature_name=FEATURES,
            )
        ]
        callbacks.append(lgb.early_stopping(100, verbose=False))
    booster = lgb.train(
        params,
        dtrain,
        num_boost_round=2000,
        valid_sets=valid_sets,
        callbacks=callbacks,
    )
    train_seconds = perf_counter() - start

    start = perf_counter()
    prediction = np.full(len(test), np.nan, dtype=float)
    valid = test[FEATURES].notna().all(axis=1).to_numpy()
    if valid.any():
        prediction[valid] = booster.predict(test.loc[valid, FEATURES].to_numpy(dtype=float))
    inference_seconds = _per_1000(perf_counter() - start, len(test))
    return ModelPrediction(
        prediction,
        train_seconds,
        inference_seconds,
        len(booster.model_to_string().encode("utf-8")),
    )


def _fit_lstm(
    train: pd.DataFrame,
    test: pd.DataFrame,
    history: pd.DataFrame,
    *,
    seed: int,
    window: int,
    hidden: int,
    epochs: int,
) -> ModelPrediction:
    import torch
    from sklearn.preprocessing import StandardScaler
    from torch import nn
    from torch.utils.data import DataLoader, TensorDataset

    if window < 1:
        raise ValueError("window must be >= 1")
    torch.manual_seed(seed)
    torch.set_num_threads(1)
    np.random.seed(seed)

    start = perf_counter()
    mask = _training_mask(train)
    train_rows = train.loc[mask]
    train_part, val_part = _time_split(train_rows)
    train_end_rows = train_rows[train_part.to_numpy()]
    val_end_rows = train_rows[val_part.to_numpy()]

    scaler = StandardScaler().fit(train_rows[FEATURES].to_numpy(dtype=float))
    x_train, y_train, _, _ = _sequence_arrays(train, train_end_rows, window)
    if len(y_train) == 0:
        raise ValueError("not enough training rows for one LSTM window")
    x_train = _scale_sequences(x_train, scaler)
    y_mean = float(y_train.mean())
    y_std = float(y_train.std()) or 1.0
    y_train_scaled = (y_train - y_mean) / y_std

    has_validation = len(val_end_rows) > 0
    if has_validation:
        x_val, y_val, _, _ = _sequence_arrays(train, val_end_rows, window)
        has_validation = len(y_val) > 0
    if has_validation:
        x_val = _scale_sequences(x_val, scaler)
        y_val_scaled = (y_val - y_mean) / y_std
    else:
        x_val, y_val_scaled = None, None

    train_ds = TensorDataset(
        torch.from_numpy(x_train.astype(np.float32)),
        torch.from_numpy(y_train_scaled.astype(np.float32)),
    )
    train_loader = DataLoader(train_ds, batch_size=128, shuffle=True)
    val_loader = None
    if x_val is not None:
        val_ds = TensorDataset(
            torch.from_numpy(x_val.astype(np.float32)),
            torch.from_numpy(y_val_scaled.astype(np.float32)),
        )
        val_loader = DataLoader(val_ds, batch_size=512, shuffle=False)

    model = _LstmRegressor(len(FEATURES), hidden)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-3)
    loss_fn = nn.MSELoss()
    best_state = copy.deepcopy(model.state_dict())
    best_loss = float("inf")
    patience = 0
    for _ in range(epochs):
        model.train()
        for xb, yb in train_loader:
            optimizer.zero_grad()
            loss = loss_fn(model(xb), yb)
            loss.backward()
            optimizer.step()
        if val_loader is None:
            continue
        model.eval()
        losses = []
        with torch.no_grad():
            for xb, yb in val_loader:
                losses.append(float(loss_fn(model(xb), yb)))
        val_loss = float(np.mean(losses))
        if val_loss < best_loss - 1e-6:
            best_loss = val_loss
            best_state = copy.deepcopy(model.state_dict())
            patience = 0
        else:
            patience += 1
            if patience >= 4:
                break
    model.load_state_dict(best_state)
    train_seconds = perf_counter() - start

    start = perf_counter()
    test_mask = test["is_day"].fillna(False) & test[TARGET].notna()
    end_rows = test.loc[test_mask]
    x_test, _, times, array_ids = _sequence_arrays(history, end_rows, window)
    prediction = np.full(len(test), np.nan, dtype=float)
    if len(times) > 0:
        x_test = _scale_sequences(x_test, scaler)
        model.eval()
        outputs = []
        with torch.no_grad():
            for start_index in range(0, len(x_test), 512):
                batch = torch.from_numpy(x_test[start_index : start_index + 512].astype(np.float32))
                outputs.append(model(batch).numpy())
        predicted = np.concatenate(outputs) * y_std + y_mean
        source = pd.MultiIndex.from_arrays([array_ids, times], names=["array_id", "timestamp"])
        target = pd.MultiIndex.from_arrays(
            [test["array_id"].to_numpy(), test.index], names=["array_id", "timestamp"]
        )
        prediction = pd.Series(predicted, index=source).reindex(target).to_numpy(dtype=float)
    inference_seconds = _per_1000(perf_counter() - start, len(test))
    buffer = io.BytesIO()
    torch.save(model.state_dict(), buffer)
    return ModelPrediction(prediction, train_seconds, inference_seconds, len(buffer.getvalue()))


class _LstmRegressor:
    """Factory that keeps the torch import out of the module import path."""

    def __new__(cls, input_size: int, hidden: int):  # noqa: N807
        from torch import nn

        class LstmRegressor(nn.Module):
            def __init__(self) -> None:
                super().__init__()
                self.lstm = nn.LSTM(input_size, hidden, batch_first=True)
                self.head = nn.Linear(hidden, 1)

            def forward(self, values):  # noqa: ANN001, ANN204
                output, _ = self.lstm(values)
                return self.head(output[:, -1]).squeeze(-1)

        return LstmRegressor()


def _scale_sequences(values: np.ndarray, scaler) -> np.ndarray:  # noqa: ANN001
    shape = values.shape
    return scaler.transform(values.reshape(-1, shape[-1])).reshape(shape)


def _sequence_arrays(
    frame: pd.DataFrame,
    end_rows: pd.DataFrame,
    window: int,
) -> tuple[np.ndarray, np.ndarray, pd.DatetimeIndex, np.ndarray]:
    """Build ``(n, window, features)`` sequences ending at ``end_rows``.

    Sequences never cross an array boundary: one array is one contiguous time series.
    Rows with a missing feature or target are dropped instead of imputed.
    """
    xs: list[np.ndarray] = []
    ys: list[np.ndarray] = []
    stamps: list[pd.DatetimeIndex] = []
    ids: list[np.ndarray] = []
    for array_id, group in frame.groupby("array_id", sort=False):
        group = group.sort_index()
        if len(group) < window:
            continue
        ends = end_rows.loc[end_rows["array_id"] == array_id].index
        if len(ends) == 0:
            continue
        positions = group.index.get_indexer(ends)
        positions = positions[positions >= window - 1]
        if len(positions) == 0:
            continue
        values = group[FEATURES].to_numpy(dtype=float)
        windows = np.lib.stride_tricks.sliding_window_view(values, (window, len(FEATURES)))[:, 0]
        xs.append(windows[positions - (window - 1)])
        ys.append(group[TARGET].to_numpy(dtype=float)[positions])
        stamps.append(group.index[positions])
        ids.append(np.full(len(positions), array_id, dtype=object))
    if not xs:
        return (
            np.empty((0, window, len(FEATURES))),
            np.empty(0),
            pd.DatetimeIndex([], tz="UTC"),
            np.empty(0, dtype=object),
        )
    x = np.concatenate(xs)
    y = np.concatenate(ys)
    times = pd.DatetimeIndex(np.concatenate([index.to_numpy() for index in stamps]), tz="UTC")
    array_ids = np.concatenate(ids)
    valid = np.isfinite(y) & np.isfinite(x).all(axis=(1, 2))
    return x[valid], y[valid], times[valid], array_ids[valid]


def _per_1000(seconds: float, rows: int) -> float:
    return seconds / rows * 1000 if rows else 0.0
