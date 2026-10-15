from __future__ import annotations

from datetime import UTC, date, datetime, timedelta
from pathlib import Path

import httpx
import pandas as pd
import pyarrow.parquet as pq
import pytest

from solarops_ml.weather import (
    COLUMNS,
    OpenMeteoClient,
    OpenMeteoError,
    Resolution,
    WeatherSource,
    fetch_weather,
    load_locations,
    month_chunks,
    parse_response,
    read_dataset,
)
from solarops_ml.weather.cli import main
from solarops_ml.weather.client import VARIABLES


def fake_payload(params: dict[str, str], *, step_s: int = 3600) -> dict:
    """Mimic Open-Meteo: values for every interval between start_date and end_date (UTC)."""
    start = datetime.fromisoformat(params["start_date"]).replace(tzinfo=UTC)
    end = datetime.fromisoformat(params["end_date"]).replace(tzinfo=UTC) + timedelta(days=1)
    times = list(range(int(start.timestamp()), int(end.timestamp()), step_s))
    block_name = "minutely_15" if "minutely_15" in params else "hourly"
    variables = params[block_name].split(",")
    block: dict[str, list] = {"time": times}
    for i, variable in enumerate(variables):
        block[variable] = [float(i + (t // step_s) % 24) for t in times]
    if "cloud_cover" in block:
        block["cloud_cover"][0] = None  # Open-Meteo sends null for missing values
    return {
        "latitude": 48.68,
        "longitude": 21.28,
        "elevation": 214.0,
        "timezone": "UTC",
        block_name: block,
    }


class Recorder:
    def __init__(self, responses: list[httpx.Response] | None = None) -> None:
        self.requests: list[httpx.Request] = []
        self.responses = responses or []

    def __call__(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        if self.responses:
            return self.responses.pop(0)
        return httpx.Response(200, json=fake_payload(dict(request.url.params)))


def make_client(recorder: Recorder, sleeps: list[float] | None = None) -> OpenMeteoClient:
    sleeps = sleeps if sleeps is not None else []
    return OpenMeteoClient(
        httpx.Client(transport=httpx.MockTransport(recorder)),
        sleep=sleeps.append,
        min_interval_s=0.0,
        backoff_s=0.5,
    )


def test_fetch_sends_utc_unixtime_and_required_variables() -> None:
    recorder = Recorder()
    client = make_client(recorder)

    frame, grid = client.fetch(
        48.72, 21.26, date(2024, 6, 20), date(2024, 6, 21), WeatherSource.HISTORICAL_FORECAST
    )

    request = recorder.requests[0]
    params = dict(request.url.params)
    assert str(request.url).startswith("https://historical-forecast-api.open-meteo.com/v1/forecast")
    assert params["timezone"] == "UTC"
    assert params["timeformat"] == "unixtime"
    assert params["wind_speed_unit"] == "ms"
    assert params["start_date"] == "2024-06-20"
    assert params["end_date"] == "2024-06-21"
    assert set(params["hourly"].split(",")) >= {
        "shortwave_radiation",
        "direct_normal_irradiance",
        "diffuse_radiation",
        "cloud_cover",
        "temperature_2m",
        "wind_speed_10m",
    }
    assert grid.latitude == 48.68 and grid.elevation == 214.0
    assert len(frame) == 48
    assert str(frame.index.tz) == "UTC"
    assert frame.index[0] == pd.Timestamp("2024-06-20T00:00Z")
    assert list(frame.columns) == list(VARIABLES.values())
    assert pd.isna(frame["cloud_cover_pct"].iloc[0])


def test_archive_source_uses_archive_endpoint() -> None:
    recorder = Recorder()
    make_client(recorder).fetch(
        48.7, 21.2, date(2020, 1, 1), date(2020, 1, 1), WeatherSource.ARCHIVE
    )
    assert recorder.requests[0].url.host == "archive-api.open-meteo.com"


def test_minutely_15_is_rejected_for_archive() -> None:
    with pytest.raises(ValueError):
        make_client(Recorder()).fetch(
            48.7,
            21.2,
            date(2020, 1, 1),
            date(2020, 1, 1),
            WeatherSource.ARCHIVE,
            Resolution.MINUTELY_15,
        )


def test_parse_minutely_15_block() -> None:
    params = {"start_date": "2024-06-20", "end_date": "2024-06-20", "minutely_15": "temperature_2m"}
    frame = parse_response(fake_payload(params, step_s=900), Resolution.MINUTELY_15)
    assert len(frame) == 96
    assert frame.index[1] - frame.index[0] == pd.Timedelta(minutes=15)
    assert frame["ghi_wm2"].isna().all()  # variables missing in the payload become NaN


def test_retries_429_honouring_retry_after_then_succeeds() -> None:
    ok_params = {
        "start_date": "2024-01-01",
        "end_date": "2024-01-01",
        "hourly": ",".join(VARIABLES),
    }
    recorder = Recorder(
        [
            httpx.Response(429, headers={"Retry-After": "3"}, json={"reason": "limit"}),
            httpx.Response(503, text="busy"),
            httpx.Response(200, json=fake_payload(ok_params)),
        ]
    )
    sleeps: list[float] = []
    frame, _ = make_client(recorder, sleeps).fetch(
        48.7, 21.2, date(2024, 1, 1), date(2024, 1, 1), WeatherSource.HISTORICAL_FORECAST
    )
    assert len(recorder.requests) == 3
    assert sleeps == [3.0, 1.0]  # Retry-After, then backoff 0.5 * 2**1
    assert len(frame) == 24


def test_bad_request_is_not_retried() -> None:
    recorder = Recorder([httpx.Response(400, json={"error": True, "reason": "bad date"})])
    with pytest.raises(OpenMeteoError, match="bad date"):
        make_client(recorder).fetch(
            48.7, 21.2, date(2024, 1, 1), date(2024, 1, 1), WeatherSource.ARCHIVE
        )
    assert len(recorder.requests) == 1


def test_gives_up_after_max_retries() -> None:
    recorder = Recorder([httpx.Response(500) for _ in range(10)])
    client = OpenMeteoClient(
        httpx.Client(transport=httpx.MockTransport(recorder)),
        sleep=lambda _: None,
        max_retries=2,
    )
    with pytest.raises(OpenMeteoError):
        client.fetch(48.7, 21.2, date(2024, 1, 1), date(2024, 1, 1), WeatherSource.ARCHIVE)
    assert len(recorder.requests) == 3


def test_throttle_keeps_min_interval_between_requests() -> None:
    sleeps: list[float] = []
    now = [100.0]
    client = OpenMeteoClient(
        httpx.Client(transport=httpx.MockTransport(Recorder())),
        sleep=sleeps.append,
        clock=lambda: now[0],
        min_interval_s=0.5,
    )
    client.fetch(48.7, 21.2, date(2024, 1, 1), date(2024, 1, 1), WeatherSource.ARCHIVE)
    client.fetch(48.7, 21.2, date(2024, 1, 2), date(2024, 1, 2), WeatherSource.ARCHIVE)
    assert sleeps == [0.5]


def test_month_chunks_cover_range_without_gaps() -> None:
    chunks = month_chunks(date(2024, 1, 15), date(2024, 3, 3))
    assert chunks == [
        (date(2024, 1, 15), date(2024, 1, 31)),
        (date(2024, 2, 1), date(2024, 2, 29)),
        (date(2024, 3, 1), date(2024, 3, 3)),
    ]
    with pytest.raises(ValueError):
        month_chunks(date(2024, 2, 1), date(2024, 1, 1))


def test_fetch_weather_chunks_by_month_and_uses_cache(tmp_path: Path) -> None:
    recorder = Recorder()
    client = make_client(recorder)
    kwargs = dict(data_dir=tmp_path, today=date(2026, 10, 15))

    frame = fetch_weather(client, 48.72, 21.26, date(2024, 1, 30), date(2024, 2, 2), **kwargs)

    assert len(recorder.requests) == 2  # January part + February part
    assert len(frame) == 4 * 24
    assert frame.index.is_monotonic_increasing and frame.index.is_unique
    assert (frame.index[1:] - frame.index[:-1] == pd.Timedelta(hours=1)).all()
    assert list(frame.columns) == COLUMNS
    assert (frame["weather_source"] == "historical-forecast").all()  # auto: >= 2022
    assert frame["latitude"].iloc[0] == 48.68

    cached = fetch_weather(client, 48.72, 21.26, date(2024, 1, 30), date(2024, 2, 2), **kwargs)
    assert len(recorder.requests) == 2  # served from parquet cache
    pd.testing.assert_frame_equal(frame, cached, check_freq=False)


def test_recent_chunks_are_not_cached(tmp_path: Path) -> None:
    recorder = Recorder()
    client = make_client(recorder)
    today = date(2026, 10, 15)
    for _ in range(2):
        fetch_weather(
            client,
            48.72,
            21.26,
            date(2026, 10, 10),
            date(2026, 10, 12),
            data_dir=tmp_path,
            today=today,
        )
    assert len(recorder.requests) == 2


def test_auto_source_falls_back_to_archive_before_2022(tmp_path: Path) -> None:
    recorder = Recorder()
    frame = fetch_weather(
        make_client(recorder), 48.72, 21.26, date(2021, 5, 1), date(2021, 5, 1), data_dir=tmp_path
    )
    assert recorder.requests[0].url.host == "archive-api.open-meteo.com"
    assert (frame["weather_source"] == "archive").all()


def test_locations_file_is_valid() -> None:
    locations = load_locations()
    assert "kosice" in locations
    assert locations["kosice"].latitude == pytest.approx(48.72)


def test_cli_fetch_by_location_writes_documented_parquet(tmp_path: Path, capsys) -> None:
    recorder = Recorder()
    out = tmp_path / "kosice.parquet"
    code = main(
        [
            "fetch",
            "--location",
            "kosice",
            "--start",
            "2024-06-20",
            "--end",
            "2024-06-21",
            "--data-dir",
            str(tmp_path),
            "--out",
            str(out),
        ],
        client=make_client(recorder),
    )
    assert code == 0
    assert "48 rows" in capsys.readouterr().out
    table = pq.read_table(out)
    assert b"CC BY 4.0" in table.schema.metadata[b"solarops.attribution"]
    assert table.column_names == ["time", *COLUMNS]
    frame = read_dataset(out)
    assert str(frame.index.tz) == "UTC"
    params = dict(recorder.requests[0].url.params)
    assert params["latitude"] == "48.72" and params["longitude"] == "21.26"


def test_cli_fetch_by_coordinates_to_csv(tmp_path: Path) -> None:
    code = main(
        [
            "fetch",
            "--lat",
            "48.1",
            "--lon",
            "17.1",
            "--start",
            "2021-03-01",
            "--end",
            "2021-03-01",
            "--source",
            "archive",
            "--format",
            "csv",
            "--data-dir",
            str(tmp_path),
        ],
        client=make_client(Recorder()),
    )
    assert code == 0
    files = list((tmp_path / "weather").glob("*.csv"))
    assert len(files) == 1
    assert files[0].name == "lat48.1000_lon17.1000_2021-03-01_2021-03-01_archive_hourly.csv"
    frame = read_dataset(files[0])
    assert frame.index[0] == pd.Timestamp("2021-03-01T00:00Z")


def test_cli_rejects_inverted_range(tmp_path: Path) -> None:
    with pytest.raises(SystemExit):
        main(
            ["fetch", "--location", "kosice", "--start", "2024-02-01", "--end", "2024-01-01"],
            client=make_client(Recorder()),
        )
