# Contributing to Statefalse

Statefalse is an independent free and open-source project licensed under
GPLv3. Contributions are welcome from individuals and organizations, subject
to the license and the contributor's right to submit the work.

## Before you start

- Do not include access tokens, passwords, private repository data or
  production configuration in commits.
- Do not include client-owned code, screenshots, logs or identifiers.
- Check the repository's `SECURITY.md` before reporting a vulnerability.
- By contributing, you confirm that you have the right to submit the work
  under the project's license.

## Backend development

The backend is an ASP.NET application targeting .NET 10. Restore, build and
test it from the repository root with the commands used by CI:

```bash
dotnet restore backend/Statefalse.Api.csproj
dotnet build backend/Statefalse.Api.csproj -c Release --no-restore
dotnet test tests-backend/Statefalse.Api.Tests.csproj -c Release
```

Use test configuration and local data only. Never use the production
`deploy/statefalse.env` while developing or testing.

## Native development

The native app is a macOS Swift/Xcode project. For day-to-day development:

```bash
cd native
bash run-local.sh
```

Run the native tests from Xcode or with the command used by
`.github/workflows/swift-tests.yml`. Snapshot tests may require a graphical
macOS session and are treated separately in headless CI.

## Shell scripts

Validate deployment scripts before submitting changes:

```bash
bash -n deploy.sh deploy/*.sh
shellcheck deploy.sh deploy/*.sh
```

Do not run a production deployment from a pull request checkout. Production
deployments are performed deliberately from `main` using `deploy.sh`.

## Pull requests

A useful pull request should include:

- a focused description of the change;
- tests or validation commands and their result;
- documentation updates when behavior or configuration changes;
- no secrets or private customer information.

Keep public examples generic. Use placeholders such as `example-org`,
`example-repo` and `ABC-123` instead of real organization identifiers.

## License

By contributing to Statefalse, you agree that your contribution is licensed
under the GNU General Public License version 3, as stated
in `LICENSE`.


