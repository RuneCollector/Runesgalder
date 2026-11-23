# ADR-0009: Licence, contribution terms, and naming

- Status: Accepted
- Date: 2026-11-23
- Deciders: karlmoriaty

## Context

Runesgalder is positioned as an open-source project from its first commit, with two equal pillars (Flow and Sandbox) and a star-growth strategy. The licence, contribution terms, name, and publication rules were decided before and during the first week of work. They need a durable record so later changes are deliberate.

## Decision

- **Licence**: MIT. The copyright holder is karlmoriaty, linked to the GitHub account. GitHub detects the licence as MIT.
- **Contributions**: Contributor Licence Agreement v1.0, effective 2026-11-23, modelled on mature individual and corporate templates. CLA Assistant is enabled with a GitHub App scoped to this repository, and the `license/cla` status check is a required merge gate on `main`. Corporate signatories send a signed copy by email; received copies are stored privately.
- **Naming**: the project name is Runesgalder. Only initial trademark and domain checks are done. No trademark registration, no domain purchase, and no social account reservation without per-time approval.
- **Publication**: the repository is public from Day 1. No packages are published and no public announcement is made before v0.1-preview. Publishing, launch, domain, and trademark actions each require explicit approval at the time they happen.
- **Security contact**: 3070687583@qq.com only. Secrets, tokens, private keys, customer data, and machine-specific paths are never committed.
- **Dependencies**: a new dependency needs a stated need, a licence check, and a security review, recorded in the relevant ADR.
- **Telemetry**: fully off by default.
- **Docs**: British English first. Chinese and German Quickstarts ship with v0.1.
- **Legal**: governed by PRC law, with exclusive jurisdiction in the courts of the project owner's domicile. An independent legal review is deferred; the owner decided on 2026-11-23 not to engage counsel for now.

## Consequences

Positive:

- A permissive licence and a clear CLA keep both personal and corporate contributions possible.
- The public repository, merge gate, and approval rules make the project auditable from the first commit.
- Publication gates prevent accidental releases or announcements.

Negative:

- The CLA adds friction for contributors; CLA Assistant mitigates it but does not remove it.
- Deferring legal review leaves the contribution terms and jurisdiction untested by counsel.
- Trademark checks are informal, so the name could clash with an existing mark.

## Alternatives considered

- **Apache-2.0.** Rejected: MIT matches the intended adoption surface, and the patent clause is not worth the extra terms for v0.1.
- **DCO instead of a CLA.** Rejected: a CLA keeps relicensing and corporate contribution options available.
- **Private development until v0.1.** Rejected: the star-growth strategy depends on early visibility, and the repository already carries tests, docs, and CI.