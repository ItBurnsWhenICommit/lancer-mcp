# Refactor Changelog

## [phase 0] baseline and safety rails

- added deterministic C# benchmark corpus under `testdata/csharp`
- added benchmark query set loader, stats, and response parser
- enforced payload guardrails for compact responses
- added hybrid benchmark CLI project
- aligned persistence batch inserts with schema constraints
- fixed integration test query invocation and null handling
- added benchmark and retrieval profile docs
- normalized commit timestamps to UTC before persistence
- fixed pgvector Dapper parameter handling to set vector type name
- improved edge resolution with a local-name fallback for method calls
- added tests for commit metadata UTC normalization and vector type handler behavior
