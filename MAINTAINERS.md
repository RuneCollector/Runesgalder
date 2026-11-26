# Maintainers

Runesgalder is an independent open-source project created by karlmoriaty, who owns the project and is the lead maintainer. Ownership and copyright are recorded in [AUTHORS](AUTHORS) and [LICENSE](LICENSE).

## Current maintainers

| GitHub | Role | Areas | Since |
| --- | --- | --- | --- |
| [@karlmoriaty](https://github.com/karlmoriaty) | Owner and lead maintainer | All areas; release approval and security contact | 2026 |

The project owner is the sole code owner in [CODEOWNERS](.github/CODEOWNERS) today. Co-maintainers are added there as they join.

## Recruiting co-maintainers

The project is recruiting one or two co-maintainers, with a goal of two to three active maintainers in total. The [project plan](docs/PROJECT_PLAN.md) targets external maintainers after v0.5; recruitment opens earlier because the current bus factor is one.

Maintenance is unpaid. This is a pre-alpha project with unstable contracts, so the work is closer to design review and careful change than to large feature delivery.

### Areas

We are looking for owners for at least one of these areas:

- **Flow**: combinators, budgets, approvals, journal and recovery semantics.
- **Sandbox**: isolation, container hardening, policy, and the escape corpus.
- **MCP and tooling**: stdio surface, client integrations, CLI, and developer experience.
- **Documentation**: quickstarts, translations, ADRs, and the project site.
- **Release and CI**: packaging, attestations, dependency hygiene, and the Linux E2E lane.
- **Security review**: threat model, policy changes, and generated-code boundaries.

### What we ask

- A few hours a week, sustained over months rather than a single burst.
- Triage issues and review pull requests in your area.
- Follow [CONTRIBUTING.md](CONTRIBUTING.md), the [CLA](CLA.md), and the [Code of Conduct](CODE_OF_CONDUCT.md).
- Disclose conflicts of interest and keep security reports private under [SECURITY.md](SECURITY.md).
- Make decisions in public issues or pull requests, except for security matters.

### What you get

- Write access and review responsibility for your area after the trial period.
- An entry in [CODEOWNERS](.github/CODEOWNERS), [AUTHORS](AUTHORS), and this file.
- Credit in release notes for the work you maintain.
- No payment; this is a volunteer open-source project.

### Path from contributor to maintainer

1. Contribute two or three focused changes with tests or documentation, so we can review how you work.
2. Open a [co-maintainer application](https://github.com/Runesgalder/Runesgalder/issues/new?template=maintainer_application.yml) describing the area you want to own and your availability.
3. Complete a four to six week trial as a reviewer and triager in that area.
4. The project owner decides, with input from the current team, and records the outcome in the application issue. Accepted co-maintainers are added to `CODEOWNERS`, `AUTHORS`, and this file.

### Decision, inactivity, and removal

The project owner makes the final decision. A co-maintainer may step down at any time. After a long period of inactivity, the owner will ask whether the role should continue; if there is no response, the person moves to an emeritus list in this file rather than being removed silently.

### Privacy

Applications are public. Share only what you are comfortable making public: a GitHub handle, the area you want to own, and a general availability range. Do not post private contact details, employer information, or identity documents. Follow-up can happen through GitHub or the project email listed in [AUTHORS](AUTHORS). Security reports must not use the application issue; follow [SECURITY.md](SECURITY.md).