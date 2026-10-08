if (args.FirstOrDefault() == "app-server") return ReadOnlyChecks.FakeServer();
return Checks.Run();
