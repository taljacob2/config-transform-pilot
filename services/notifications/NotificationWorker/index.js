// Stub only -- never built or run in CI. Exists so this pilot has a real, non-.NET consumer of
// a .env resource, the same way LegacyGateway.Framework exists to prove no TargetFramework
// coupling. What matters here is .env's own shape (QUEUE_URL/LOG_LEVEL/etc.) and this project's
// location in the repo tree, not that the service does anything real.
console.log("NotificationWorker starting against " + process.env.QUEUE_URL);
