# Unofficial adapter reviews

One file per unofficial provider:

```text
reviews/<provider>.md
```

Each review must follow `../UNOFFICIAL_ADAPTERS.md` and record:

- source repository/commit
- dependency/SBOM summary
- audit findings and resolutions
- isolation design (container/process/host)
- event fixtures and contract tests
- live smoke-test evidence
- ToS/legal notes
- kill-switch test
- approval signature and date

No review file means no automatic rewards for that provider. The default remains manual linking + custom webhooks.
