CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TABLE devices (
    id TEXT PRIMARY KEY,
    hostname TEXT NULL,
    display_name TEXT NULL,
    last_seen_at TIMESTAMPTZ NOT NULL,
    last_reviewed_at TIMESTAMPTZ NULL
);

CREATE TABLE ingest_cursors (
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    stream TEXT NOT NULL,
    last_sequence BIGINT NOT NULL,
    last_batch_id UUID NOT NULL,
    last_sent_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (device_id, stream)
);

CREATE TABLE web_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id UUID NOT NULL UNIQUE,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    domain TEXT NOT NULL,
    title TEXT NULL,
    url TEXT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    browser TEXT NULL,
    received_at TIMESTAMPTZ NOT NULL,
    CHECK (char_length(device_id) <= 64),
    CHECK (char_length(domain) <= 255),
    CHECK (title IS NULL OR char_length(title) <= 512),
    CHECK (url IS NULL OR char_length(url) <= 2048),
    CHECK (browser IS NULL OR char_length(browser) <= 64)
);

CREATE INDEX ix_web_events_device_timestamp ON web_events(device_id, timestamp);

CREATE TABLE web_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL UNIQUE,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    domain TEXT NOT NULL,
    title TEXT NULL,
    url TEXT NULL,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    CHECK (end_at > start_at),
    CHECK (char_length(domain) <= 255),
    CHECK (title IS NULL OR char_length(title) <= 512),
    CHECK (url IS NULL OR char_length(url) <= 2048)
);

CREATE INDEX ix_web_device_start ON web_sessions(device_id, start_at);
CREATE INDEX ix_web_device_domain_start ON web_sessions(device_id, domain, start_at);

CREATE TABLE app_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL UNIQUE,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    process_name TEXT NOT NULL,
    window_title TEXT NULL,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    CHECK (end_at > start_at),
    CHECK (char_length(process_name) <= 255),
    CHECK (window_title IS NULL OR char_length(window_title) <= 512)
);

CREATE INDEX ix_app_device_start ON app_sessions(device_id, start_at);
CREATE INDEX ix_app_device_process_start ON app_sessions(device_id, process_name, start_at);

CREATE TABLE idle_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL UNIQUE,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    CHECK (end_at > start_at)
);

CREATE INDEX ix_idle_device_start ON idle_sessions(device_id, start_at);
