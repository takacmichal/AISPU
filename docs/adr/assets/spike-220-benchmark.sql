create table devices(id uuid primary key);
insert into devices select gen_random_uuid() from generate_series(1,100) ;
create table telemetry_readings(
 device_id uuid not null, ts timestamptz not null, message_id uuid not null,
 status smallint not null, ac_power_w real, dc_power_w real, voltage_v real, current_a real, frequency_hz real, temperature_c real, day_wh double precision, total_wh double precision,
 primary key(device_id, ts));
insert into telemetry_readings
select d.id, t, gen_random_uuid(), 1, random()*5000, random()*5200, 230, random()*20, 50, 20+random()*30, random()*30000, 1e7+random()*1e6
from generate_series(timestamptz '2026-01-01', timestamptz '2026-12-31 23:55', interval '5 minutes') t, devices d order by t, d.id;
create index brin_ts on telemetry_readings using brin(ts) with (pages_per_range=32);
analyze telemetry_readings;
select pg_size_pretty(pg_total_relation_size('telemetry_readings')) total, pg_size_pretty(pg_relation_size('telemetry_readings')) heap, pg_size_pretty(pg_relation_size('telemetry_readings_pkey')) pkey, pg_size_pretty(pg_relation_size('brin_ts')) brin;
create temp table pick as select id from devices limit 1;
\echo Q1
explain (analyze, costs off, timing off, summary on) select ts,ac_power_w from telemetry_readings where device_id=(select id from pick) and ts>='2026-06-01' and ts<'2026-06-02' order by ts;
\echo Q2
explain (analyze, costs off, timing off, summary on) select date_trunc('hour',ts) h, avg(ac_power_w), max(ac_power_w) from telemetry_readings where device_id=(select id from pick) and ts>='2026-06-01' and ts<'2026-07-01' group by 1 order by 1;
\echo Q3
explain (analyze, costs off, timing off, summary on) select device_id, date_trunc('day',ts) d, avg(ac_power_w) from telemetry_readings where ts>='2026-06-01' and ts<'2026-07-01' group by 1,2;
\echo Q4
explain (analyze, costs off, timing off, summary on) select l.* from devices dv cross join lateral (select device_id, ts, ac_power_w from telemetry_readings r where r.device_id=dv.id order by ts desc limit 1) l;
\echo Q5
explain (analyze, costs off, timing off, summary on) insert into telemetry_readings select id, timestamptz '2027-01-01', gen_random_uuid(),1,1,1,1,1,1,1,1,1 from devices on conflict (device_id, ts) do nothing;
create table telemetry_hourly as select device_id, date_trunc('hour',ts) hour_start, avg(ac_power_w)::real avg_power_w, max(ac_power_w)::real max_power_w, (max(total_wh)-min(total_wh)) energy_wh, count(*)::smallint n from telemetry_readings group by 1,2;
alter table telemetry_hourly add primary key(device_id,hour_start);
select pg_size_pretty(pg_total_relation_size('telemetry_hourly')) hourly_size;
