# Security

## Supported versions

Security fixes go into the latest release of `ChromaDotNet.Client`, `ChromaDotNet.Client.DependencyInjection`, `ChromaDotNet.Client.Backup` and `ChromaDotNet.Client.Cli` on NuGet, of the native files of the command `chromadotnet` on the GitHub release, and of the image `ghcr.io/chromadotnet/chromadotnet`. Earlier versions do not get fixes: update to the latest one.

## Reporting a vulnerability

Please do not report a vulnerability in a public issue, discussion or pull request.

Report it privately on GitHub instead: [Report a vulnerability](https://github.com/ChromaDotNet/ChromaDB.Client/security/advisories/new), in the **Security** tab of this repository. Only the maintainers see the report.

Include the version of the package, the version of Chroma or Chroma Cloud, and the steps to reproduce the problem.

## What happens next

- We acknowledge the report within 7 days.
- We confirm or rule out the vulnerability within 14 days, and keep you informed of the progress.
- A confirmed vulnerability is fixed in a new release. A GitHub security advisory follows the release, with credit to you unless you prefer otherwise.
- Please keep the details private until the advisory is published, and for at most 90 days from the report.

## How the packages are published

- Only the CI publishes the packages, the native files and the image, from the version tags of this repository; the packages with [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): no NuGet API key is kept by a person or in the repository.
- The organization requires two-factor authentication.
- The workflows pin their actions to commit hashes. Dependabot and CodeQL check the dependencies and the code.

## Verifying a package

From 2.10.2 on, each GitHub release has the packages that the CI built from the version tag, with:

- their Sigstore signatures (`.sigstore.json`), made by the release workflow of this repository;
- their build provenance (`provenance-<tag>.intoto.jsonl`), attested by the CI.

nuget.org serves the same packages with its repository signature (`.signature.p7s`) added: the release workflow checks that every other file is the same. Earlier releases have no build provenance.

To verify a package downloaded from the release, with Cosign 3 or later:

```
cosign verify-blob ChromaDotNet.Client.2.11.0.nupkg --bundle ChromaDotNet.Client.2.11.0.nupkg.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Client/[.]github/workflows/release-assets[.]yml@' --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

With the GitHub CLI:

```
gh attestation verify ChromaDotNet.Client.2.11.0.nupkg --bundle provenance-v2.11.0.intoto.jsonl --repo ChromaDotNet/ChromaDB.Client --signer-workflow ChromaDotNet/ChromaDB.Client/.github/workflows/ci.yml --source-ref refs/tags/v2.11.0
```

## Verifying the native files

From 2.22.0 on, each GitHub release has the command `chromadotnet` as native files, with their SHA-256 in `SHA256SUMS` and a Sigstore signature for each (`<file>.sigstore.json`), made by the release workflow. They have no build provenance.

```
sha256sum --check --ignore-missing SHA256SUMS
cosign verify-blob chromadotnet-linux-x64 --bundle chromadotnet-linux-x64.sigstore.json --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Client/[.]github/workflows/release-assets[.]yml@' --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

## Verifying the image

From 2.22.0 on, the image `ghcr.io/chromadotnet/chromadotnet` of each release is signed by its digest with Cosign, and its build provenance is attested by the release workflow and kept in the registry:

```
cosign verify ghcr.io/chromadotnet/chromadotnet:2.22.0 --certificate-identity-regexp '^https://github[.]com/ChromaDotNet/ChromaDB[.]Client/[.]github/workflows/release-assets[.]yml@' --certificate-oidc-issuer https://token.actions.githubusercontent.com
gh attestation verify oci://ghcr.io/chromadotnet/chromadotnet:2.22.0 --repo ChromaDotNet/ChromaDB.Client --signer-workflow ChromaDotNet/ChromaDB.Client/.github/workflows/release-assets.yml --source-ref refs/tags/v2.22.0
```
