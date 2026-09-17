DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'admin') THEN
        CREATE ROLE admin LOGIN PASSWORD 'admin';
    ELSE
        ALTER ROLE admin WITH LOGIN PASSWORD 'admin';
    END IF;
END
$$;

SELECT 'CREATE DATABASE "TradingProject" OWNER admin'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'TradingProject')\gexec

GRANT ALL PRIVILEGES ON DATABASE "TradingProject" TO admin;
ALTER DATABASE "TradingProject" OWNER TO admin;
