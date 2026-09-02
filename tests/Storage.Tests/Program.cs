#nullable enable

TempDirPathProviderTests.Run();
SqliteShutdownTests.Run();
RunLogSchemaMigrationTests.Run();
RunLogSchemaReleaseContractTests.Run();

Console.WriteLine("All Storage tests passed.");
