#!/usr/bin/env python3
"""Reset and re-seed the SolarOps demo data in the LOCAL dev database (issue #188).

What it does (idempotent, safe to run repeatedly):
  1. Refuses to run unless both the DB host and the API host are localhost.
  2. Deletes all demo rows with SQL (public API has no delete for quotes/contracts/installations).
     Demo rows are recognised by customer e-mail '*@demo.solarops.local' and catalog code 'DEMO-*'.
     Nothing else in the database is touched.
  3. Re-creates the demo data through the public API (so business rules and numbering apply).

Only the Python standard library is used. SQL runs through a local `psql` if available, otherwise
through `docker compose exec postgres psql` (the compose service from the repo root).

Configuration (environment, local defaults only):
  DEMO_API_URL    default http://localhost:5080
  DEMO_DB_URL     default Host=localhost;Port=5432;Database=solarops;Username=solarops;Password=solarops
  DEMO_EMAIL      default demo@solarops.local   (user with role Admin)
  DEMO_PASSWORD   default DemoPass12345

Usage: python3 scripts/demo/reset-demo.py [--no-seed]
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlparse

API_URL = os.environ.get("DEMO_API_URL", "http://localhost:5080").rstrip("/")
DB_URL = os.environ.get(
    "DEMO_DB_URL", "Host=localhost;Port=5432;Database=solarops;Username=solarops;Password=solarops"
)
EMAIL = os.environ.get("DEMO_EMAIL", "demo@solarops.local")
PASSWORD = os.environ.get("DEMO_PASSWORD", "DemoPass12345")
TECH_EMAIL = "technik@demo.solarops.local"
TECH_PASSWORD = os.environ.get("DEMO_TECH_PASSWORD", "DemoTech12345")
LOCAL_HOSTS = {"localhost", "127.0.0.1", "::1", "[::1]"}
REPO_ROOT = Path(__file__).resolve().parents[2]
DEMO_DOMAIN = "demo.solarops.local"


def die(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    sys.exit(1)


# ---------------------------------------------------------------- guards + DB

def parse_conn(conn: str) -> dict[str, str]:
    parts = {}
    for item in conn.split(";"):
        if "=" in item:
            key, value = item.split("=", 1)
            parts[key.strip().lower()] = value.strip()
    return parts


def guard() -> dict[str, str]:
    db = parse_conn(DB_URL)
    host = db.get("host", "")
    if host.lower() not in LOCAL_HOSTS:
        die(f"refusing to run: DB host '{host}' is not localhost (DEMO_DB_URL).")
    api_host = urlparse(API_URL).hostname or ""
    if api_host.lower() not in LOCAL_HOSTS:
        die(f"refusing to run: API host '{api_host}' is not localhost (DEMO_API_URL).")
    for key in ("database", "username"):
        if not db.get(key):
            die(f"DEMO_DB_URL is missing '{key}'.")
    return db


def run_sql(db: dict[str, str], sql: str) -> str:
    base = ["-v", "ON_ERROR_STOP=1", "-X", "-q", "-At", "-U", db["username"], "-d", db["database"]]
    if shutil.which("psql"):
        cmd = ["psql", "-h", db["host"], "-p", db.get("port", "5432"), *base]
        env = {**os.environ, "PGPASSWORD": db.get("password", "")}
        proc = subprocess.run(cmd, input=sql, capture_output=True, text=True, env=env)
    elif shutil.which("docker"):
        cmd = ["docker", "compose", "exec", "-T", "postgres", "psql", *base]
        proc = subprocess.run(cmd, input=sql, capture_output=True, text=True, cwd=REPO_ROOT)
    else:
        die("neither psql nor docker found; cannot run SQL.")
    if proc.returncode != 0:
        die(f"SQL failed: {proc.stderr.strip()}")
    return proc.stdout


RESET_SQL = f"""
BEGIN;
CREATE TEMP TABLE demo_customers ON COMMIT DROP AS
  SELECT id FROM customers WHERE lower(email) LIKE '%@{DEMO_DOMAIN}';
CREATE TEMP TABLE demo_installations ON COMMIT DROP AS
  SELECT id FROM installations WHERE customer_id IN (SELECT id FROM demo_customers);
DELETE FROM connection_requests WHERE customer_id IN (SELECT id FROM demo_customers)
  OR installation_id IN (SELECT id FROM demo_installations);
DELETE FROM installation_devices WHERE installation_id IN (SELECT id FROM demo_installations);
DELETE FROM installations WHERE id IN (SELECT id FROM demo_installations);
DELETE FROM contracts WHERE customer_id IN (SELECT id FROM demo_customers);
DELETE FROM quotes WHERE customer_id IN (SELECT id FROM demo_customers);
DELETE FROM fve_design_versions WHERE design_id IN
  (SELECT id FROM fve_designs WHERE customer_id IN (SELECT id FROM demo_customers));
DELETE FROM fve_designs WHERE customer_id IN (SELECT id FROM demo_customers);
DELETE FROM supply_points WHERE customer_id IN (SELECT id FROM demo_customers);
DELETE FROM customers WHERE id IN (SELECT id FROM demo_customers);
DELETE FROM catalog_components WHERE code LIKE 'DEMO-%';
COMMIT;
"""

# ---------------------------------------------------------------- API helpers

TOKEN = ""


def api(method: str, path: str, body: object | None = None, expect: tuple[int, ...] = (200, 201, 204)):
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    if TOKEN:
        headers["Authorization"] = f"Bearer {TOKEN}"
    req = urllib.request.Request(API_URL + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            status, raw = resp.status, resp.read()
    except urllib.error.HTTPError as err:
        status, raw = err.code, err.read()
    except urllib.error.URLError as err:
        die(f"API not reachable at {API_URL}: {err.reason}")
    if status not in expect:
        die(f"{method} {path} -> {status}: {raw.decode(errors='replace')[:600]}")
    return json.loads(raw) if raw else None


def login() -> None:
    global TOKEN
    TOKEN = ""
    res = api("POST", "/api/auth/login", {"email": EMAIL, "password": PASSWORD})
    TOKEN = res["accessToken"]


# ---------------------------------------------------------------- seed data

def address(street: str, city: str, postal: str) -> dict:
    return {"street": street, "city": city, "postalCode": postal, "country": "SK"}


def ensure_technician() -> str:
    """Technician account (not removed by the reset, so this step is find-or-create)."""
    api("POST", "/api/auth/register", {"email": TECH_EMAIL, "password": TECH_PASSWORD}, expect=(201, 409))
    users = api("GET", f"/api/users?q={TECH_EMAIL}")["items"]
    user = next(u for u in users if u["email"].lower() == TECH_EMAIL)
    api("PUT", f"/api/users/{user['id']}/roles", {"roles": ["Technician"]})
    return user["id"]


def seed() -> dict:
    ids: dict[str, str] = {}

    # Catalog
    def component(key: str, type_: str, code: str, name: str, manufacturer: str | None, power: int | None, price: float):
        res = api("POST", "/api/components", {
            "type": type_, "code": code, "name": name, "manufacturer": manufacturer,
            "powerW": power, "unitPrice": price})
        ids[key] = res["id"]

    component("panel", "Panel", "DEMO-PAN-450", "JA Solar JAM54S30-450 (demo)", "JA Solar", 450, 145.0)
    component("inverter", "Inverter", "DEMO-INV-10K", "Fronius Symo 10.0-3-M (demo)", "Fronius", 10000, 1450.0)
    component("labor", "Labor", "DEMO-LAB-INST", "Montaz FVE, hodina (demo)", None, None, 55.0)

    # Customers + supply points
    def customer(key: str, first: str, last: str, city: str, street: str, postal: str, phone: str, eic: str, amps: int):
        c = api("POST", "/api/customers", {
            "type": "Person", "firstName": first, "lastName": last,
            "email": f"{last.lower()}@{DEMO_DOMAIN}", "phone": phone,
            "address": address(street, city, postal),
            "note": "Demo data (reset-demo.py)"})
        sp = api("POST", f"/api/customers/{c['id']}/supply-points", {
            "name": "Rodinny dom", "eicCode": eic, "address": address(street, city, postal),
            "mainBreakerAmps": amps})
        ids[f"{key}_customer"] = c["id"]
        ids[f"{key}_supply"] = sp["id"]

    customer("novak", "Jan", "Novak", "Nitra", "Hviezdoslavova 12", "949 01", "+421 905 111 222", "24ZDEMO00000001A", 25)
    customer("kovacova", "Maria", "Kovacova", "Zilina", "Kuzmanyho 5", "010 01", "+421 905 333 444", "24ZDEMO00000002B", 25)
    customer("horvath", "Peter", "Horvath", "Kosice", "Hlavna 40", "040 01", "+421 905 555 666", "24ZDEMO00000003C", 32)

    def design(key: str, notes: str, until: str) -> None:
        """until: requested | surveyed | configured"""
        d = api("POST", "/api/designs", {
            "customerId": ids[f"{key}_customer"], "supplyPointId": ids[f"{key}_supply"], "notes": notes})
        ids[f"{key}_design"] = d["id"]
        if until == "requested":
            return
        api("POST", f"/api/designs/{d['id']}/site-survey", {"notes": "Obhliadka: sedlova strecha, bez tienenia."})
        if until == "surveyed":
            return
        api("PUT", f"/api/designs/{d['id']}/parameters", {
            "latitude": 48.31, "longitude": 18.08, "annualConsumptionKwh": 5200,
            "consumptionProfile": "Household",
            "roofPlanes": [{"name": "Juh", "azimuthDeg": 0, "tiltDeg": 35, "usableAreaM2": 60,
                            "shadingLossPercent": 3, "roofType": "PitchedTile"}]})
        api("POST", f"/api/designs/{d['id']}/versions", {
            "panelId": ids["panel"], "panelCount": 22, "inverterId": ids["inverter"]})

    def quote_contract(key: str, sign: bool) -> None:
        q = api("POST", "/api/quotes", {
            "customerId": ids[f"{key}_customer"], "vatRatePercent": 23,
            "note": "Demo ponuka FVE 9,9 kWp",
            "lines": [{"componentId": ids["panel"], "quantity": 22},
                      {"componentId": ids["inverter"], "quantity": 1},
                      {"componentId": ids["labor"], "quantity": 24}]})
        ids[f"{key}_quote"] = q["id"]
        api("POST", f"/api/quotes/{q['id']}/send", {"validityDays": 30})
        api("POST", f"/api/quotes/{q['id']}/accept", {})
        c = api("POST", "/api/contracts", {"quoteId": q["id"]})
        ids[f"{key}_contract"] = c["id"]
        if sign:
            signed = api("POST", f"/api/contracts/{c['id']}/sign", {
                "supplyPointId": ids[f"{key}_supply"], "installationName": "FVE 9,9 kWp (demo)"})
            ids[f"{key}_installation"] = signed["installationId"]
            api("POST", f"/api/contracts/{c['id']}/deposit/pay", {"amount": 1500})

    # Novak: complete flow up to a commissioned installation (the "finished customer" of the demo)
    design("novak", "Zakaznik chce FVE na rodinny dom", "configured")
    quote_contract("novak", sign=True)
    inst = ids["novak_installation"]
    tech = ensure_technician()
    ids["technician"] = tech
    api("PUT", f"/api/installations/{inst}/schedule", {"scheduledStart": "2026-11-04", "scheduledEnd": "2026-11-05"})
    api("PUT", f"/api/installations/{inst}/technicians", {"technicianIds": [tech]})
    for state in ("ComponentsOrdered", "ComponentsReceived", "Mounting"):
        api("POST", f"/api/installations/{inst}/transitions", {"targetState": state})
    for serial, type_, manuf, model, power in (
        ("DEMO-FRO-0001", "Inverter", "Fronius", "Symo 10.0-3-M", 10000),
        ("DEMO-JA-0001", "Panel", "JA Solar", "JAM54S30-450", 450),
        ("DEMO-JA-0002", "Panel", "JA Solar", "JAM54S30-450", 450),
    ):
        api("POST", f"/api/installations/{inst}/devices", {
            "type": type_, "serialNumber": serial, "manufacturer": manuf, "model": model, "powerW": power})
    for state in ("MountingDone", "Inspection", "InspectionPassed", "Commissioned"):
        api("POST", f"/api/installations/{inst}/transitions", {"targetState": state})
    cr = api("POST", "/api/connection-requests", {
        "installationId": inst, "eicCode": "24ZDEMO00000001A", "ratedAcPowerKw": 10, "mainBreakerAmps": 25,
        "attachments": [
            {"kind": "SingleLineDiagram", "fileName": "schema.pdf", "contentType": "application/pdf", "sizeBytes": 120000},
            {"kind": "OwnerConsent", "fileName": "suhlas-vlastnika.pdf", "contentType": "application/pdf", "sizeBytes": 80000}]})
    ids["novak_connection_request"] = cr["id"]

    # Kovacova: live demo starts here (customer + supply point + design request only)
    design("kovacova", "Zakaznicka ziadost o ponuku (zivy start dema)", "requested")

    # Horvath: plan B (everything up to a signed contract + Planned installation)
    design("horvath", "Zalozny zakaznik pre plan B", "configured")
    quote_contract("horvath", sign=True)
    return ids


def summary() -> str:
    """Structural fingerprint (no ids/timestamps) used to compare two runs."""
    lines = []
    for c in sorted(api("GET", "/api/customers?pageSize=100")["items"], key=lambda x: x["email"]):
        if not c["email"].endswith(DEMO_DOMAIN):
            continue
        lines.append(f"customer {c['email']} {c['status']}")
    for d in sorted(api("GET", "/api/designs"), key=lambda x: x["requestNotes"] or ""):
        lines.append(f"design '{d['requestNotes']}' {d['status']}")
    for q in api("GET", "/api/quotes"):
        lines.append(f"quote {q['status']} gross={q['grossTotal']}")
    for c in api("GET", "/api/contracts"):
        lines.append(f"contract {c['number']} {c['status']} deposit={c['depositStatus']}")
    for i in sorted(api("GET", "/api/installations"), key=lambda x: x["name"]):
        lines.append(f"installation '{i['name']}' {i['state']}")
    return "\n".join(sorted(lines))


def main() -> None:
    db = guard()
    print(f"DB: {db['host']}:{db.get('port', '5432')}/{db['database']}  API: {API_URL}")
    login()
    print("Deleting previous demo rows ...")
    run_sql(db, RESET_SQL)
    if "--no-seed" in sys.argv:
        print("Done (no seed).")
        return
    print("Seeding through the API ...")
    ids = seed()
    print("\nDemo ids:")
    for key, value in ids.items():
        print(f"  {key:28} {value}")
    print(f"\nLogin: {EMAIL} / (DEMO_PASSWORD)\nDone.")


if __name__ == "__main__":
    main()
