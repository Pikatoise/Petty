-- Run explicitly as petty_owner after each successful migration, before starting API.
GRANT USAGE ON SCHEMA public TO petty_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO petty_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO petty_app;
REVOKE ALL ON TABLE public."__EFMigrationsHistory" FROM petty_app;
GRANT SELECT ON TABLE public."__EFMigrationsHistory" TO petty_app;
