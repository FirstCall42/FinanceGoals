-- Runs on every deployment. The app user reads and writes data in the app schema and can
-- run its procedures, but cannot change the schema. GRANT is idempotent.

GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::app TO app_data;
