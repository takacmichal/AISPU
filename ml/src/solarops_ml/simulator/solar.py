"""Solar geometry and a simple clear-sky model (numpy only, no pvlib dependency).

Solar position uses the Spencer (1971) Fourier series for declination and equation of
time (NOAA solar calculator formulation), accurate to well below 1 degree, which is plenty
for a data simulator. Clear-sky irradiance is a Meinel-style model: direct beam attenuated
by air mass, diffuse as a fixed fraction of the beam.
"""

from __future__ import annotations

import numpy as np
import pandas as pd

SOLAR_CONSTANT_WM2 = 1361.0
ALBEDO = 0.2
DIFFUSE_FRACTION_OF_BEAM = 0.1


def solar_position(times: pd.DatetimeIndex, latitude: float, longitude: float) -> pd.DataFrame:
    """Sun zenith and compass azimuth (0 = north, clockwise) in degrees for UTC ``times``."""
    if times.tz is None:
        raise ValueError("times must be timezone-aware (UTC)")
    utc = times.tz_convert("UTC")
    hour = utc.hour + utc.minute / 60 + utc.second / 3600
    doy = utc.dayofyear.to_numpy()
    g = 2 * np.pi / 365 * (doy - 1 + (np.asarray(hour) - 12) / 24)
    eqtime = 229.18 * (
        0.000075
        + 0.001868 * np.cos(g)
        - 0.032077 * np.sin(g)
        - 0.014615 * np.cos(2 * g)
        - 0.040849 * np.sin(2 * g)
    )  # minutes
    decl = (
        0.006918
        - 0.399912 * np.cos(g)
        + 0.070257 * np.sin(g)
        - 0.006758 * np.cos(2 * g)
        + 0.000907 * np.sin(2 * g)
        - 0.002697 * np.cos(3 * g)
        + 0.00148 * np.sin(3 * g)
    )  # radians
    true_solar_min = np.asarray(hour) * 60 + eqtime + 4 * longitude
    hour_angle = np.radians(true_solar_min / 4 - 180)
    lat = np.radians(latitude)
    cos_zen = np.sin(lat) * np.sin(decl) + np.cos(lat) * np.cos(decl) * np.cos(hour_angle)
    zenith = np.arccos(np.clip(cos_zen, -1, 1))
    # Azimuth measured from south towards west (Meeus), shifted to compass degrees.
    az_south = np.arctan2(
        np.sin(hour_angle), np.cos(hour_angle) * np.sin(lat) - np.tan(decl) * np.cos(lat)
    )
    azimuth = (np.degrees(az_south) + 180) % 360
    return pd.DataFrame({"zenith_deg": np.degrees(zenith), "azimuth_deg": azimuth}, index=times)


def clear_sky(
    zenith_deg: np.ndarray, day_of_year: np.ndarray
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Clear-sky ``(ghi, dni, dhi)`` in W/m2; zero when the sun is below the horizon."""
    zen = np.asarray(zenith_deg, dtype=float)
    cos_z = np.cos(np.radians(zen))
    up = cos_z > 0
    zen_safe = np.where(up, zen, 0.0)
    air_mass = 1 / (np.cos(np.radians(zen_safe)) + 0.50572 * (96.07995 - zen_safe) ** -1.6364)
    ecc = 1 + 0.033 * np.cos(2 * np.pi * (np.asarray(day_of_year) - 3) / 365)
    dni = np.where(up, SOLAR_CONSTANT_WM2 * ecc * 0.7 ** (air_mass**0.678), 0.0)
    dhi = DIFFUSE_FRACTION_OF_BEAM * dni
    ghi = dni * np.where(up, cos_z, 0.0) + dhi
    return ghi, dni, dhi


def plane_irradiance(
    zenith_deg: np.ndarray,
    azimuth_deg: np.ndarray,
    ghi: np.ndarray,
    dni: np.ndarray,
    dhi: np.ndarray,
    tilt: float,
    azimuth: float,
) -> np.ndarray:
    """Global tilted irradiance (GTI) on a fixed plane, isotropic sky model.

    ``azimuth`` uses the project convention (0 = south, -90 = east, 90 = west, 180 = north),
    the same as ``locations.toml`` and the Open-Meteo GTI request.
    """
    panel_az = np.radians((azimuth + 180) % 360)  # to compass degrees
    zen = np.radians(zenith_deg)
    sun_az = np.radians(azimuth_deg)
    beta = np.radians(tilt)
    cos_aoi = np.cos(zen) * np.cos(beta) + np.sin(zen) * np.sin(beta) * np.cos(sun_az - panel_az)
    beam = dni * np.clip(cos_aoi, 0, None)
    sky = dhi * (1 + np.cos(beta)) / 2
    ground = ghi * ALBEDO * (1 - np.cos(beta)) / 2
    return np.where(np.asarray(zenith_deg) < 90, beam + sky + ground, 0.0)
