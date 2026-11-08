#!/usr/bin/env python3
"""Reset and re-seed the SolarOps demo data in the LOCAL dev database (issue #188).

What it does (idempotent, safe to run repeatedly):
  1. Refuses to run unless both the DB host and the API host are localhost.
  2. Deletes all demo rows with SQL (public API has no delete for quotes/contracts/installations).
     Demo rows are recognised by customer e-mail '*@demo.solarops.local' and catalog code 'DEMO-*'.
     Nothing else in the database is touched.
  3. Re-creates the demo data through the public API (so business rules and numbering apply).
  4. Fills Novak's plant with production (unless --no-production): 30 days of 5-minute telemetry from the
     simulator in ml/, driven by the real weather of those days, the ML prediction for the same days and a
     power drop over the last two days, which the ML detection turns into an underproduction alarm and a
     service ticket. Needs `uv` (ml/), internet (Open-Meteo) and the ML service behind the API.
  5. Prints a structural fingerprint (no ids/timestamps) of the demo data, so two runs can be diffed.

Accounts: Admin (DEMO_EMAIL), Sales, Designer, Operator, two technicians and the customer portal account of Novak
(see ROLE_ACCOUNTS, PORTAL_EMAIL); docs/frontend-prechod.md walks through the frontend with them.

Demo scope (11 customers, each with one supply point):
  - Novak: commissioned installation, closed remote ticket.
  - Kovacova: live demo start (design request only).
  - Horvath: signed contract, installation Planned (plan B).
  - Hudak: quote sent only.
  - Balazova: quote accepted, contract not signed yet.
  - Mrazova: signed, connection request InfoRequested (installation Planned).
  - Sabo: mounting in progress (partial checklist).
  - Kral: inspection (mounting done, no revision report yet).
  - Lacko: commissioned, ticket in VisitScheduled with a rescheduled visit and a public comment.
  - Gajdos: commissioned, ticket closed through a full visit (protocol, materials, verification).
  - Vargova: commissioned, ticket Triaged with a technician assigned.

Only the Python standard library is used. SQL runs through a local `psql` if available, otherwise
through `docker compose exec postgres psql` (the compose service from the repo root).

Configuration (environment, local defaults only):
  DEMO_API_URL    default http://localhost:5080
  DEMO_DB_URL     default Host=localhost;Port=5432;Database=solarops;Username=solarops;Password=solarops
  DEMO_EMAIL      default demo@solarops.local   (user with role Admin)
  DEMO_PASSWORD   default DemoPass12345

Usage: python3 scripts/demo/reset-demo.py [--no-seed] [--no-production]

Deployed environment (Azure, docs/demo/scenar.md): python3 scripts/demo/reset-demo.py --remote
  Seeds an HTTPS API. The repository is public, so the default passwords are not used: DEMO_EMAIL/DEMO_PASSWORD
  are the admin login, DEMO_ACCOUNT_PASSWORD and DEMO_TECH_PASSWORD must be set. Without DEMO_DB_URL it cannot
  delete anything and refuses to run when demo customers exist. With DEMO_DB_URL (the Azure server, your IP
  allowed in its firewall) it deletes the demo rows first, using psql or `docker run postgres:16-alpine psql`
  over TLS.
"""

from __future__ import annotations

import base64
import itertools
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import urlparse

API_URL = os.environ.get("DEMO_API_URL", "http://localhost:5080").rstrip("/")
DB_URL = os.environ.get(
    "DEMO_DB_URL", "Host=localhost;Port=5432;Database=solarops;Username=solarops;Password=solarops"
)
EMAIL = os.environ.get("DEMO_EMAIL", "demo@solarops.local")
PASSWORD = os.environ.get("DEMO_PASSWORD", "DemoPass12345")
TECH_EMAIL = "technik@demo.solarops.local"
TECH2_EMAIL = "technik2@demo.solarops.local"
TECH_PASSWORD = os.environ.get("DEMO_TECH_PASSWORD", "DemoTech12345")
# Password of the created role and portal accounts; locally the same as the admin login.
ACCOUNT_PASSWORD = os.environ.get("DEMO_ACCOUNT_PASSWORD", PASSWORD)
REMOTE = "--remote" in sys.argv
# One account per remaining role for the walkthrough (docs/frontend-prechod.md); all use DEMO_PASSWORD.
ROLE_ACCOUNTS = {
    "obchod@demo.solarops.local": ["Sales"],
    "projektant@demo.solarops.local": ["Designer"],
    "operator@demo.solarops.local": ["Operator"],
}
# Customer portal account of Jan Novak (the same e-mail as the customer record).
PORTAL_EMAIL = "novak@demo.solarops.local"
LOCAL_HOSTS = {"localhost", "127.0.0.1", "::1", "[::1]"}
REPO_ROOT = Path(__file__).resolve().parents[2]
DEMO_DOMAIN = "demo.solarops.local"

# Revision report and photo payloads (placeholder bytes, the API only stores them).
DEMO_PDF_BASE64 = base64.b64encode(b"%PDF-1.4 demo revision report").decode()
DEMO_PHOTO_BASE64 = "/9j/4AAQSkZJRg=="  # the same tiny JPEG header as in Tickets.http

# Location and roof of every demo design; the production of Novak's plant is simulated for the same place.
DEMO_LATITUDE, DEMO_LONGITUDE = 48.31, 18.08
DEMO_TILT_DEG, DEMO_AZIMUTH_DEG = 35, 0
NOVAK_PANELS = 22
PANEL_KWP, INVERTER_AC_KW = 0.45, 10.0
# The weather backfill and the past prediction accept at most 31 days, both ending yesterday.
PRODUCTION_DAYS = 30
# Output drops to 55 % for the last two days: inside the 72 h detection lookback and still going on at the end.
FAULT_DAYS, FAULT_SEVERITY = 2, 0.45
SIMULATOR_SEED = 188
SLOW_CALL_TIMEOUT_S = 300

CHECKLIST_CODES = ("SiteSafety", "MountingStructure", "PanelsMounted", "DcCabling", "InverterMounted", "Protections",
                   "AcConnection", "Grounding", "StringMeasurements", "PhotoDocumentation", "SiteCleanup")


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


def guard_remote() -> None:
    if urlparse(API_URL).scheme != "https":
        die(f"--remote needs an https API (DEMO_API_URL), got '{API_URL}'.")
    for name in ("DEMO_EMAIL", "DEMO_PASSWORD", "DEMO_ACCOUNT_PASSWORD", "DEMO_TECH_PASSWORD"):
        if not os.environ.get(name):
            die(f"--remote needs {name}; the defaults are public in the repository.")
    if "--no-seed" in sys.argv and "DEMO_DB_URL" not in os.environ:
        die("--remote --no-seed needs DEMO_DB_URL to delete the demo data.")


def remote_db() -> dict[str, str] | None:
    """The deployed database, only when DEMO_DB_URL is given explicitly."""
    if "DEMO_DB_URL" not in os.environ:
        return None
    db = parse_conn(DB_URL)
    for key in ("host", "database", "username", "password"):
        if not db.get(key):
            die(f"DEMO_DB_URL is missing '{key}'.")
    return db


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
    if REMOTE and not shutil.which("psql") and shutil.which("docker"):
        # Azure PostgreSQL requires TLS; the password goes through the environment, not the command line.
        cmd = ["docker", "run", "--rm", "-i", "-e", "PGPASSWORD", "-e", "PGSSLMODE=require", "postgres:16-alpine",
               "psql", "-h", db["host"], "-p", db.get("port", "5432"), *base]
        env = {**os.environ, "PGPASSWORD": db["password"]}
        proc = subprocess.run(cmd, input=sql, capture_output=True, text=True, env=env)
    elif shutil.which("psql"):
        cmd = ["psql", "-h", db["host"], "-p", db.get("port", "5432"), *base]
        env = {**os.environ, "PGPASSWORD": db.get("password", ""), **({"PGSSLMODE": "require"} if REMOTE else {})}
        proc = subprocess.run(cmd, input=sql, capture_output=True, text=True, env=env)
    elif shutil.which("docker") and not REMOTE:
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
-- service_tickets has no FK to customers or installations, so it is deleted explicitly.
-- Its visits, materials, device replacements, comments, attachments, assignments and state changes cascade.
DELETE FROM service_tickets WHERE customer_id IN (SELECT id FROM demo_customers)
  OR installation_id IN (SELECT id FROM demo_installations);
DELETE FROM connection_requests WHERE customer_id IN (SELECT id FROM demo_customers)
  OR installation_id IN (SELECT id FROM demo_installations);
-- Telemetry, device API keys and strings reference the installation or its devices (not cascaded).
DELETE FROM telemetry_readings WHERE device_id IN
  (SELECT id FROM installation_devices WHERE installation_id IN (SELECT id FROM demo_installations));
DELETE FROM device_api_keys WHERE installation_id IN (SELECT id FROM demo_installations);
-- Predictions, alarms and stored weather have no FK to the installation.
DELETE FROM production_predictions WHERE installation_id IN (SELECT id FROM demo_installations);
DELETE FROM underproduction_alarms WHERE installation_id IN (SELECT id FROM demo_installations);
DELETE FROM weather_forecasts WHERE installation_id IN (SELECT id FROM demo_installations);
DELETE FROM panel_strings WHERE installation_id IN (SELECT id FROM demo_installations);
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


def api(method: str, path: str, body: object | None = None, expect: tuple[int, ...] = (200, 201, 204),
        api_key: str | None = None, timeout: int = 30):
    """JSON call with the admin token, or with a device API key (X-Api-Key) when api_key is given."""
    data = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    if api_key:
        headers["X-Api-Key"] = api_key
    elif TOKEN:
        headers["Authorization"] = f"Bearer {TOKEN}"
    req = urllib.request.Request(API_URL + path, data=data, headers=headers, method=method)
    # URLError means the request was not sent (connect or TLS failed), so even a POST is safe to repeat.
    for attempt in range(1, 4):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                status, raw = resp.status, resp.read()
            break
        except urllib.error.HTTPError as err:
            status, raw = err.code, err.read()
            break
        except urllib.error.URLError as err:
            if attempt == 3:
                die(f"API not reachable at {API_URL}: {err.reason}")
            time.sleep(5 * attempt)
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


def ensure_user(email: str, password: str, roles: list[str]) -> str:
    """Login account with the given roles (accounts are not removed by the reset, so this is find-or-create)."""
    api("POST", "/api/auth/register", {"email": email, "password": password}, expect=(201, 409))
    users = api("GET", f"/api/users?q={email}")["items"]
    user = next(u for u in users if u["email"].lower() == email)
    api("PUT", f"/api/users/{user['id']}/roles", {"roles": roles})
    return user["id"]


def ensure_technician(email: str = TECH_EMAIL) -> str:
    return ensure_user(email, TECH_PASSWORD, ["Technician"])


def eic_code(number: int) -> str:
    """EIC in the format ^\\d{2}[A-Z][A-Z0-9-]{12}[A-Z0-9]$ (16 characters), unique per demo customer."""
    return f"24ZDEMO{number:08d}A"


def utc_iso(moment: datetime) -> str:
    return moment.astimezone(timezone.utc).isoformat(timespec="seconds")


def seed() -> dict:
    ids: dict[str, str] = {}
    serials = itertools.count(1)

    # Catalog
    def component(key: str, type_: str, code: str, name: str, manufacturer: str | None, power: int | None, price: float):
        res = api("POST", "/api/components", {
            "type": type_, "code": code, "name": name, "manufacturer": manufacturer,
            "powerW": power, "unitPrice": price}, expect=(201, 409))
        if res and "id" in res:
            ids[key] = res["id"]
        else:
            # Only --remote keeps the catalog (no SQL delete); reuse the component from an interrupted run.
            ids[key] = next(c["id"] for c in api("GET", f"/api/components?q={code}") if c["code"] == code)

    component("panel", "Panel", "DEMO-PAN-450", "JA Solar JAM54S30-450 (demo)", "JA Solar", 450, 145.0)
    component("inverter", "Inverter", "DEMO-INV-10K", "Fronius Symo 10.0-3-M (demo)", "Fronius", 10000, 1450.0)
    component("labor", "Labor", "DEMO-LAB-INST", "Montaz FVE, hodina (demo)", None, None, 55.0)

    # Customers + supply points
    # Accounts for the other roles and the customer portal (Novak sees only his own plant and tickets)
    for email, roles in ROLE_ACCOUNTS.items():
        ensure_user(email, ACCOUNT_PASSWORD, roles)
    portal_user = ensure_user(PORTAL_EMAIL, ACCOUNT_PASSWORD, ["Customer"])

    def customer(key: str, first: str, last: str, city: str, street: str, postal: str, phone: str, eic: str, amps: int,
                 user_id: str | None = None):
        c = api("POST", "/api/customers", {
            "type": "Person", "firstName": first, "lastName": last,
            "email": f"{last.lower()}@{DEMO_DOMAIN}", "phone": phone,
            "address": address(street, city, postal),
            "note": "Demo data (reset-demo.py)", "userId": user_id})
        sp = api("POST", f"/api/customers/{c['id']}/supply-points", {
            "name": "Rodinny dom", "eicCode": eic, "address": address(street, city, postal),
            "mainBreakerAmps": amps})
        ids[f"{key}_customer"] = c["id"]
        ids[f"{key}_supply"] = sp["id"]

    customer("novak", "Jan", "Novak", "Nitra", "Hviezdoslavova 12", "949 01", "+421 905 111 222", "24ZDEMO00000001A", 25,
             user_id=portal_user)
    customer("kovacova", "Maria", "Kovacova", "Zilina", "Kuzmanyho 5", "010 01", "+421 905 333 444", "24ZDEMO00000002B", 25)
    customer("horvath", "Peter", "Horvath", "Kosice", "Hlavna 40", "040 01", "+421 905 555 666", "24ZDEMO00000003C", 32)
    customer("hudak", "Tomas", "Hudak", "Bratislava", "Mlynska 14", "821 08", "+421 905 700 101", eic_code(4), 25)
    customer("balazova", "Eva", "Balazova", "Trnava", "Hlavna 22", "917 01", "+421 905 700 102", eic_code(5), 20)
    customer("mrazova", "Lucia", "Mrazova", "Trencin", "Mierova 8", "911 01", "+421 905 700 103", eic_code(6), 25)
    customer("sabo", "Martin", "Sabo", "Presov", "Hlavna 60", "080 01", "+421 905 700 104", eic_code(7), 25)
    customer("kral", "Jozef", "Kral", "Banska Bystrica", "Namestie SNP 3", "974 01", "+421 905 700 105", eic_code(8), 32)
    customer("lacko", "Zuzana", "Lacko", "Michalovce", "Vihorlatska 10", "071 01", "+421 905 700 106", eic_code(9), 25)
    customer("gajdos", "Pavol", "Gajdos", "Levice", "Kupecka 7", "934 01", "+421 905 700 107", eic_code(10), 25)
    customer("vargova", "Katarina", "Vargova", "Poprad", "Stefanikova 14", "058 01", "+421 905 700 108", eic_code(11), 20)

    def design(key: str, notes: str, until: str, panels: int = 22) -> None:
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
            "latitude": DEMO_LATITUDE, "longitude": DEMO_LONGITUDE, "annualConsumptionKwh": 5200,
            "consumptionProfile": "Household",
            "roofPlanes": [{"name": "Juh", "azimuthDeg": DEMO_AZIMUTH_DEG, "tiltDeg": DEMO_TILT_DEG, "usableAreaM2": 60,
                            "shadingLossPercent": 3, "roofType": "PitchedTile"}]})
        api("POST", f"/api/designs/{d['id']}/versions", {
            "panelId": ids["panel"], "panelCount": panels, "inverterId": ids["inverter"]})

    def quote_contract(key: str, label: str, panels: int, stage: str) -> None:
        """stage: quote (sent), contract (accepted, contract not signed), signed (installation created)."""
        kwp = f"{panels * 0.45:.1f}".replace(".", ",")
        q = api("POST", "/api/quotes", {
            "customerId": ids[f"{key}_customer"], "vatRatePercent": 23,
            "note": f"Demo ponuka FVE {kwp} kWp",
            "lines": [{"componentId": ids["panel"], "quantity": panels},
                      {"componentId": ids["inverter"], "quantity": 1},
                      {"componentId": ids["labor"], "quantity": 24}]})
        ids[f"{key}_quote"] = q["id"]
        api("POST", f"/api/quotes/{q['id']}/send", {"validityDays": 30})
        if stage == "quote":
            return
        api("POST", f"/api/quotes/{q['id']}/accept", {})
        c = api("POST", "/api/contracts", {"quoteId": q["id"]})
        ids[f"{key}_contract"] = c["id"]
        if stage == "contract":
            return
        signed = api("POST", f"/api/contracts/{c['id']}/sign", {
            "supplyPointId": ids[f"{key}_supply"], "installationName": f"FVE {kwp} kWp {label} (demo)"})
        ids[f"{key}_installation"] = signed["installationId"]
        api("POST", f"/api/contracts/{c['id']}/deposit/pay", {"amount": 1500})

    def connection_request(key: str, inst: str, eic: str, amps: int, until: str) -> None:
        """until: info (distributor asked for more data) or concluded (full approval chain)."""
        cr = api("POST", "/api/connection-requests", {
            "installationId": inst, "eicCode": eic, "ratedAcPowerKw": 10, "mainBreakerAmps": amps,
            "attachments": [
                {"kind": "SingleLineDiagram", "fileName": "schema.pdf", "contentType": "application/pdf", "sizeBytes": 120000},
                {"kind": "OwnerConsent", "fileName": "suhlas-vlastnika.pdf", "contentType": "application/pdf", "sizeBytes": 80000}]})
        cr_id = cr["id"]
        ids[f"{key}_connection_request"] = cr_id
        api("POST", f"/api/connection-requests/{cr_id}/submit")
        if until == "info":
            api("POST", f"/api/connection-requests/{cr_id}/decision", {
                "decision": "RequestInfo",
                "note": "Doplnte fotodokumentaciu rozvadzaca a typovy stitok invertora."})
            return
        # The distributor must approve before the installation can be scheduled
        api("POST", f"/api/connection-requests/{cr_id}/decision", {"decision": "Approve"})
        api("POST", f"/api/connection-requests/{cr_id}/review-conditions")
        api("POST", f"/api/connection-requests/{cr_id}/send-contract")
        api("POST", f"/api/connection-requests/{cr_id}/customer-signed")
        api("POST", f"/api/connection-requests/{cr_id}/submit-signed-contract")
        api("POST", f"/api/connection-requests/{cr_id}/conclude")

    def schedule(inst: str, technician_ids: list[str], offset_days: int) -> None:
        # Windows are staggered per installation: the API rejects overlaps of the same technician.
        start = date.today() + timedelta(days=offset_days)  # must not be in the past (API validation)
        api("PUT", f"/api/installations/{inst}/schedule",
            {"scheduledStart": start.isoformat(), "scheduledEnd": (start + timedelta(days=1)).isoformat()})
        api("PUT", f"/api/installations/{inst}/technicians", {"technicianIds": technician_ids})

    def start_mounting(inst: str) -> None:
        for state in ("ComponentsOrdered", "ComponentsReceived", "Mounting"):
            api("POST", f"/api/installations/{inst}/transitions", {"targetState": state})

    def register_devices(inst: str, panel_count: int) -> None:
        specs = [("Inverter", "Fronius", "Symo 10.0-3-M", 10000, "FRO")]
        specs += [("Panel", "JA Solar", "JAM54S30-450", 450, "JA")] * panel_count
        for type_, manuf, model, power, prefix in specs:
            api("POST", f"/api/installations/{inst}/devices", {
                "type": type_, "serialNumber": f"DEMO-{prefix}-{next(serials):04d}",
                "manufacturer": manuf, "model": model, "powerW": power})

    def checklist_done(inst: str, codes: tuple[str, ...]) -> None:
        for code in codes:
            api("PUT", f"/api/installations/{inst}/checklist/{code}", {"done": True})

    def to_inspection(inst: str) -> None:
        # Mounting can be completed only when every checklist step is done (checked by the API)
        api("POST", f"/api/installations/{inst}/transitions", {"targetState": "MountingDone"})
        api("POST", f"/api/installations/{inst}/transitions", {"targetState": "Inspection"})

    def commission(inst: str, accepted_by: str) -> None:
        today = date.today()
        api("POST", f"/api/installations/{inst}/revision-reports", {
            "inspectedOn": today.isoformat(), "inspectorName": "Ing. Revizny technik (demo)",
            "inspectorCertificateNumber": "RT-DEMO-001", "result": "Passed",
            "validUntil": today.replace(year=today.year + 5).isoformat(), "findings": None,
            "fileName": "revizna-sprava.pdf", "contentType": "application/pdf",
            "contentBase64": DEMO_PDF_BASE64})
        api("POST", f"/api/installations/{inst}/commission", {
            "commissionedOn": today.isoformat(), "acceptedBy": accepted_by, "note": "Demo"})

    def commissioned_installation(key: str, inst: str, tech_ids: list[str], offset_days: int, panel_count: int,
                                  accepted_by: str) -> None:
        schedule(inst, tech_ids, offset_days)
        start_mounting(inst)
        register_devices(inst, panel_count)
        checklist_done(inst, CHECKLIST_CODES)
        to_inspection(inst)
        commission(inst, accepted_by)

    # Ticket helpers (service desk, Admin acts on every ticket and visit)
    def create_ticket(key: str, title: str, description: str, priority: str, attachments: list[dict] | None = None) -> str:
        t = api("POST", "/api/tickets", {
            "installationId": ids[f"{key}_installation"], "title": title, "description": description,
            "priority": priority, "attachments": attachments or []})
        ids[f"{key}_ticket"] = t["id"]
        return t["id"]

    def triage(ticket_id: str, priority: str, note: str) -> None:
        api("PUT", f"/api/tickets/{ticket_id}/triage", {"priority": priority, "note": note})

    def transition(ticket_id: str, state: str, note: str) -> None:
        api("POST", f"/api/tickets/{ticket_id}/transition", {"state": state, "note": note})

    def assign(ticket_id: str, technician_id: str) -> None:
        api("PUT", f"/api/tickets/{ticket_id}/technician", {"technicianId": technician_id})

    def plan_visit(ticket_id: str, start: datetime, minutes: int) -> str:
        visit = api("POST", f"/api/tickets/{ticket_id}/visits", {"start": utc_iso(start), "estimatedMinutes": minutes})
        return visit["id"]

    def ticket_note(ticket_id: str, body: str) -> None:
        api("POST", f"/api/tickets/{ticket_id}/comments", {"body": body, "isPublic": True})

    # Customers, supply points and designs
    now = datetime.now(timezone.utc).replace(microsecond=0)
    tech = ensure_technician()
    tech2 = ensure_technician(TECH2_EMAIL)
    ids["technician"] = tech
    ids["technician2"] = tech2

    # Novak: complete flow up to a commissioned installation (the "finished customer" of the demo)
    design("novak", "Zakaznik chce FVE na rodinny dom", "configured", panels=NOVAK_PANELS)
    quote_contract("novak", "Novak", NOVAK_PANELS, "signed")
    inst = ids["novak_installation"]
    connection_request("novak", inst, "24ZDEMO00000001A", 25, until="concluded")
    commissioned_installation("novak", inst, [tech], offset_days=3, panel_count=2, accepted_by="Jan Novak")

    # Novak: a fault reported by phone, solved remotely and closed (no visit)
    t = create_ticket("novak", "Invertor hlasi chybu (demo)",
                      "Od rana FVE nevyraba, na invertore svieti cervena LED.", "Medium",
                      [{"fileName": "porucha.jpg", "contentType": "image/jpeg", "contentBase64": DEMO_PHOTO_BASE64}])
    triage(t, "Medium", "Nahlasene telefonicky")
    transition(t, "RemoteDiagnosis", "Kontrola logov invertora na diaľku")
    api("POST", f"/api/tickets/{t}/close", {"note": "Po resete invertora vyroba obnovena"})

    # Kovacova: live demo starts here (customer + supply point + design request only)
    design("kovacova", "Zakaznicka ziadost o ponuku (zivy start dema)", "requested")

    # Horvath: plan B (everything up to a signed contract + Planned installation)
    design("horvath", "Zalozny zakaznik pre plan B", "configured")
    quote_contract("horvath", "Horvath", 22, "signed")

    # Hudak: quote sent, not accepted yet
    design("hudak", "Zakaznik po obhliadke, chce ponuku na 20 panelov", "configured", panels=20)
    quote_contract("hudak", "Hudak", 20, "quote")

    # Balazova: quote accepted, contract created but not signed
    design("balazova", "Zakaznicka ziadost, ponuka prijata", "configured", panels=18)
    quote_contract("balazova", "Balazova", 18, "contract")

    # Mrazova: signed contract, connection request waiting for the distributor's information request
    design("mrazova", "Zakaznik chce FVE a vodic do elektromobilu", "configured")
    quote_contract("mrazova", "Mrazova", 22, "signed")
    connection_request("mrazova", ids["mrazova_installation"], eic_code(6), 25, until="info")

    # Sabo: mounting in progress, partial checklist, one inverter registered
    design("sabo", "Zakaznik chce FVE na garaz a dom", "configured", panels=20)
    quote_contract("sabo", "Sabo", 20, "signed")
    inst = ids["sabo_installation"]
    connection_request("sabo", inst, eic_code(7), 25, until="concluded")
    schedule(inst, [tech], offset_days=5)
    start_mounting(inst)
    register_devices(inst, panel_count=0)
    checklist_done(inst, CHECKLIST_CODES[:3])

    # Kral: inspection, mounting completed, revision report not recorded yet
    design("kral", "Zakaznik chce FVE na hospodarsku budovu", "configured", panels=24)
    quote_contract("kral", "Kral", 24, "signed")
    inst = ids["kral_installation"]
    connection_request("kral", inst, eic_code(8), 32, until="concluded")
    schedule(inst, [tech2], offset_days=7)
    start_mounting(inst)
    register_devices(inst, panel_count=2)
    checklist_done(inst, CHECKLIST_CODES)
    to_inspection(inst)

    # Lacko: commissioned; a planned visit for a ticket with a rescheduled date and a public comment
    design("lacko", "Zakaznicka ziadost, FVE s akumulaciou", "configured")
    quote_contract("lacko", "Lacko", 22, "signed")
    inst = ids["lacko_installation"]
    connection_request("lacko", inst, eic_code(9), 25, until="concluded")
    commissioned_installation("lacko", inst, [tech2], offset_days=9, panel_count=2, accepted_by="Zuzana Lacko")
    t = create_ticket("lacko", "Nizsia vyroba v slnecnych dnoch (demo)",
                      "Zakaznik hlasi nizsiu vyrobu nez v minulom roku.", "Medium")
    triage(t, "Medium", "Zakaznik poziadal o kontrolu")
    assign(t, tech2)
    transition(t, "RemoteDiagnosis", "Porovnanie dat s predoslym rokom")
    visit_day = (now + timedelta(days=2)).replace(hour=8, minute=0, second=0, microsecond=0)
    visit = plan_visit(t, visit_day, 120)
    api("PUT", f"/api/visits/{visit}/schedule", {
        "start": utc_iso(visit_day.replace(hour=13)), "estimatedMinutes": 120,
        "reason": "Zakaznik poziadal o neskorsi termin"})
    ticket_note(t, "Technik sa ozve do 24 hodin, termin potvrdime.")

    # Gajdos: commissioned; ticket closed through a full visit (protocol, materials, verification)
    design("gajdos", "Zakaznik chce FVE a stratu zo zvysovania ceny elektriny", "configured", panels=20)
    quote_contract("gajdos", "Gajdos", 20, "signed")
    inst = ids["gajdos_installation"]
    connection_request("gajdos", inst, eic_code(10), 25, until="concluded")
    commissioned_installation("gajdos", inst, [tech], offset_days=11, panel_count=2, accepted_by="Pavol Gajdos")
    t = create_ticket("gajdos", "Vypadok vyroby po burke (demo)",
                      "Po burke FVE od vcera nevyraba, invertor je v chybovom stave.", "High")
    triage(t, "High", "Vypadok celej vyroby")
    assign(t, tech)
    transition(t, "RemoteDiagnosis", "Kontrola logov invertora na diaľku")
    # Plan must be in the future and the protocol times must be in the past (both checked by the API).
    visit = plan_visit(t, now + timedelta(minutes=15), 90)
    api("POST", f"/api/visits/{visit}/start")
    api("PUT", f"/api/visits/{visit}/materials", {"materials": [
        {"name": "DC konektor MC4", "quantity": 2, "unit": "ks"},
        {"name": "Kabel 6 mm2", "quantity": 3.5, "unit": "m"}]})
    api("POST", f"/api/visits/{visit}/report", {
        "cause": "InverterFault", "findings": "Invertor hlasi chybu izolacie DC.",
        "workDone": "Vymeneny DC konektor, restart invertora.",
        "arrivedAt": utc_iso(now - timedelta(minutes=30)), "departedAt": utc_iso(now - timedelta(minutes=5))})
    api("POST", f"/api/tickets/{t}/verify")
    api("POST", f"/api/tickets/{t}/close", {"note": "Vyroba obnovena"})

    # Vargova: commissioned; ticket triaged with a technician assigned, visit not planned yet
    design("vargova", "Zakaznicka ziadost, FVE na chatu v Tatrach", "configured", panels=24)
    quote_contract("vargova", "Vargova", 24, "signed")
    inst = ids["vargova_installation"]
    connection_request("vargova", inst, eic_code(11), 20, until="concluded")
    commissioned_installation("vargova", inst, [tech], offset_days=13, panel_count=2, accepted_by="Katarina Vargova")
    t = create_ticket("vargova", "Kontrola vyroby po uvedeni do prevadzky (demo)",
                      "Zakaznik chce skontrolovat monitoring a vyrobu v prvom mesiaci.", "Low")
    triage(t, "Low", "Rutinna kontrola")
    assign(t, tech)

    return ids


def run_ml(*args: str) -> None:
    """Run a module of the ml/ package through uv (the simulator needs numpy and pandas)."""
    uv = shutil.which("uv")
    if not uv:
        die("uv not found; install it (ml/README.md) or run with --no-production.")
    proc = subprocess.run([uv, "run", "--quiet", "python", "-m", *args], cwd=REPO_ROOT / "ml",
                          capture_output=True, text=True)
    if proc.returncode != 0:
        die(f"{args[0]} failed: {proc.stderr.strip()[-1500:]}")


def seed_production(ids: dict) -> dict:
    """Production of Novak's plant: telemetry, ML prediction and an underproduction alarm with a ticket.

    The ML prediction comes from the Open-Meteo archive stored by the weather backfill; the simulator gets the
    same archive, so the healthy days match the prediction and only the injected power drop is an anomaly.
    """
    inst = ids["novak_installation"]
    today = datetime.now(timezone.utc).date()
    first, last = today - timedelta(days=PRODUCTION_DAYS), today - timedelta(days=1)
    period = {"from": first.isoformat(), "to": last.isoformat()}
    kwp = NOVAK_PANELS * PANEL_KWP

    inverter = next(d for d in api("GET", f"/api/installations/{inst}/devices?type=Inverter"))
    key = api("POST", f"/api/installations/{inst}/api-keys")["apiKey"]

    print(f"  weather {first}..{last} (Open-Meteo archive, every installation with coordinates) ...")
    # Weather and ML calls are synchronous and slow on the smallest Azure replicas.
    backfill = api("POST", "/api/weather/backfill", period, timeout=SLOW_CALL_TIMEOUT_S)
    status = next((i["status"] for i in backfill["items"] if i["installationId"] == inst), None)
    if status != "Updated":
        die(f"weather backfill for Novak's installation ended with {status}: {backfill}")
    past = api("POST", f"/api/installations/{inst}/analysis/past", period, timeout=SLOW_CALL_TIMEOUT_S)

    print(f"  telemetry from the simulator, power drop of {FAULT_SEVERITY:.0%} over the last {FAULT_DAYS} days ...")
    fault_start = datetime.combine(today - timedelta(days=FAULT_DAYS), datetime.min.time(), timezone.utc)
    with tempfile.TemporaryDirectory() as tmp:
        weather_file, batches_file = Path(tmp, "weather.csv"), Path(tmp, "batches.json")
        coords = ("--lat", str(DEMO_LATITUDE), "--lon", str(DEMO_LONGITUDE))
        # Open-Meteo labels an hour by its end: the last hour of yesterday is labelled today 00:00 (like the backfill).
        run_ml("solarops_ml.weather", "fetch", *coords, "--start", first.isoformat(), "--end", today.isoformat(),
               "--source", "archive", "--format", "csv", "--no-cache", "--out", str(weather_file))
        run_ml("solarops_ml.simulator", *coords, "--tilt", str(DEMO_TILT_DEG), "--azimuth", str(DEMO_AZIMUTH_DEG),
               "--capacity-kwp", str(kwp), "--dc-ac-ratio", str(kwp / INVERTER_AC_KW),
               "--weather", str(weather_file), "--seed", str(SIMULATOR_SEED),
               "--start", f"{first.isoformat()}T00:00Z", "--end", f"{today.isoformat()}T00:00Z",
               "--device-id", inverter["id"], "--installation-id", inst,
               "--fault", f"power_drop,{fault_start.isoformat()},{today.isoformat()}T00:00Z,{FAULT_SEVERITY}",
               "--out", str(batches_file))
        batches = json.loads(batches_file.read_text(encoding="utf-8"))
    stored = sum(api("POST", "/api/telemetry", batch, expect=(202,), api_key=key)["stored"] for batch in batches)

    print("  ML analysis (prediction + underproduction detection) ...")
    analysis = api("POST", f"/api/installations/{inst}/analysis", timeout=SLOW_CALL_TIMEOUT_S)
    return {"readings": stored, "predictedPastHours": past["predictedHours"], **analysis}


def summary() -> str:
    """Structural fingerprint (no ids, timestamps or sequence numbers) used to compare two runs."""
    lines = []
    customers = api("GET", "/api/customers?pageSize=100")["items"]
    demo_customer_ids = {c["id"] for c in customers if c["email"].endswith(DEMO_DOMAIN)}
    for c in sorted(customers, key=lambda x: x["email"]):
        if not c["email"].endswith(DEMO_DOMAIN):
            continue
        lines.append(f"customer {c['email']} {c['status']}")
    for d in sorted(api("GET", "/api/designs"), key=lambda x: x["requestNotes"] or ""):
        lines.append(f"design '{d['requestNotes']}' {d['status']}")
    for q in api("GET", "/api/quotes"):
        lines.append(f"quote {q['status']} gross={q['grossTotal']}")
    for c in api("GET", "/api/contracts"):
        lines.append(f"contract {c['status']} deposit={c['depositStatus']}")
    installations = [i for i in api("GET", "/api/installations") if i["customerId"] in demo_customer_ids]
    for i in sorted(installations, key=lambda x: x["name"]):
        lines.append(f"installation '{i['name']}' {i['state']}")
    tickets = [t for t in api("GET", "/api/tickets?pageSize=100")["items"] if t["customerId"] in demo_customer_ids]
    for t in sorted(tickets, key=lambda x: x["title"]):
        lines.append(f"ticket '{t['title']}' {t['state']} priority={t['priority']}")
        for v in api("GET", f"/api/tickets/{t['id']}/visits")["visits"]:
            lines.append(f"  visit of '{t['title']}' {v['state']}")
    return "\n".join(sorted(lines))


def main() -> None:
    if REMOTE:
        guard_remote()
        db = remote_db()
        print(f"API: {API_URL}  DB: {db['host'] if db else 'not used, nothing is deleted'}")
        login()
        if db:
            print("Deleting previous demo rows ...")
            run_sql(db, RESET_SQL)
        elif any(c["email"].endswith(DEMO_DOMAIN) for c in api("GET", "/api/customers?pageSize=100")["items"]):
            die("demo customers already exist; set DEMO_DB_URL to delete them first (docs/demo/scenar.md).")
    else:
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
    if "--no-production" not in sys.argv:
        print("Production of Novak's plant ...")
        production = seed_production(ids)
        print("  " + ", ".join(f"{k}={production[k]}" for k in (
            "readings", "predictedPastHours", "alarms", "ongoingAlarms", "ticketsCreated")))
    print("\nDemo ids:")
    for key, value in ids.items():
        print(f"  {key:28} {value}")
    print("\nStructure fingerprint:")
    print(summary())
    print(f"\nLogin: {EMAIL} / (DEMO_PASSWORD)\nDone.")


if __name__ == "__main__":
    main()
