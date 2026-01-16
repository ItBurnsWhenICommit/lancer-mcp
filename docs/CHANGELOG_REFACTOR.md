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

## [phase 1] csharp symbol-first index

- canonicalized C# qualified names (namespace + containing type + parameter types)

## [phase 2] fast symbol retrieval

- added retrieval profile plumbing with Fast default and profile metadata
- added symbol tokenization and literal token capture for C# symbols
- added symbol_search entries with snippets for sparse symbol retrieval
- added symbol_search schema + indexes and persistence wiring
- implemented Fast profile symbol retrieval with compact "why" signals

## [phase 3] similarity without embeddings

- added SimHash fingerprint service and symbol fingerprint builder
- added symbol_fingerprints schema + repository and persistence wiring
- implemented `similar:<symbol-id>` routing with error metadata for missing seeds/fingerprints
- added banded candidate lookup, Hamming distance ranking, and post-filter terms
- added snippet lookup by symbol id for compact similarity results
- include error metadata in compact response payloads

## [phase 4A] schema safety for optional embeddings

- added schema verification test for embedding_jobs, embeddings.dims, and job indexes
- added embedding_jobs table and performance indexes for job claiming
- added embeddings.dims column to schema
- documented embedding_jobs and dims in SCHEMA.md
