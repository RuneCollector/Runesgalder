# Corporate CLA process

This document describes how the Project Owner handles Corporate Contributor Licence Agreements for Runesgalder. The template is [CORPORATE_CLA.md](../CORPORATE_CLA.md). The individual agreement is [CLA.md](../CLA.md).

## When a Corporate CLA is required

A Corporate CLA is required when Contributions are made on behalf of a corporation or another legal entity, including contributions made by employees within the scope of their employment.

A Corporate CLA is not required for personal contributions made outside an employment relationship. Those are covered by the individual CLA.

## Process

1. **Request the template.** The Corporation downloads [CORPORATE_CLA.md](../CORPORATE_CLA.md), or asks the Project Owner for a PDF copy.
2. **Complete Schedule A and Schedule B.** Schedule A identifies the entity and the authorised signatory. Schedule B lists each Designated Contributor with name, GitHub username, and contact email.
3. **Sign.** An authorised signatory signs and dates the signature block.
4. **Send the signed copy.** Email a PDF scan to 3070687583@qq.com with the subject `Corporate CLA — <legal entity name>`. Do not open a pull request to submit the signed copy.
5. **Acknowledgement.** The Project Owner reviews the copy and normally acknowledges it within five business days. The Project Owner may ask for clarification or an updated Schedule B.
6. **Record.** The Project Owner records the Corporation in a private register and confirms coverage before treating entity-owned Contributions as covered.

## Private register

The private register is held outside the repository. It records, for each Corporation:

- legal entity name and jurisdiction;
- date received and date acknowledged;
- the authorised signatory;
- current Designated Contributors;
- the date coverage ends, if terminated.

Signed agreements and the register contain personal data. They are not committed to the repository, attached to issues or pull requests, or published.

## Pull requests

CLA Assistant records individual signatures and reports the `license/cla` status check on pull requests. The bot does not verify corporate coverage.

For a pull request that includes entity-owned Contributions:

- each committer still signs the individual CLA through CLA Assistant, so the status check can pass;
- the Project Owner checks the private register to confirm the Corporation is covered and the committers are listed in Schedule B;
- if coverage is missing or a contributor is not listed, the Project Owner asks for a completed Corporate CLA or an updated Schedule B before merging.

## Changes to Schedule B

Send an updated Schedule B, signed by an authorised signatory, by email. The Project Owner records the change and acknowledges it in the same way as the original agreement.

When a Designated Contributor leaves the Corporation, the Corporation should remove them from Schedule B. Contributions they submitted while authorised remain covered by the licences already granted.

## Termination

The Corporation may end authorisation for future Contributions by written notice. Licences for Contributions submitted before termination survive. The Project Owner may remove any Contribution at any time under section 8 of the agreement.

## Contact

The only contact address for Corporate CLA matters is 3070687583@qq.com.