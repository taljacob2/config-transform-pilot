# Stub only -- never built or run in CI. Exists so this pilot has a real, non-.NET, non-Node.js
# consumer of a YAML resource, the same way NotificationWorker exists for .env. What matters here
# is config.yaml's own shape (nested keys, not just flat KEY=VALUE) and this project's location in
# the repo tree, not that the service does anything real.
import yaml

with open("config.yaml") as f:
    config = yaml.safe_load(f)

print(f"ReportingService starting with LogLevel={config['LogLevel']}")
