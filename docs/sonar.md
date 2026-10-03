# Sonar analysis

The [Sonar workflow](../.github/workflows/sonar.yml) scans pushes to `main`, pull requests from branches in this repository, and manual runs. Fork and Dependabot pull requests skip the job because they cannot access its analysis secret. The existing CI workflow continues to run build, style, and test checks separately.

## Repository setup

Create or connect the repository's project in SonarQube Cloud and disable automatic analysis for that project before enabling CI analysis. In GitHub **Settings > Secrets and variables > Actions**, configure:

| Setting | Type | Value |
| --- | --- | --- |
| `SONAR_PROJECT_KEY` | Variable | SonarQube Cloud project key |
| `SONAR_ORGANIZATION` | Variable | SonarQube Cloud organization key |
| `SONAR_TOKEN` | Secret | Analysis token with permission to analyze this project |

Missing configuration fails the job with the missing setting's name. The workflow does not print token values. Connect the SonarQube Cloud project to the GitHub repository for pull request decoration, and require `Sonar quality gate` in the repository's merge rules where appropriate.

## Scope and checks

SonarScanner for .NET wraps a clean Release build of `PenguinTools.slnx`. Multi-language analysis is enabled for supported files owned by this repository. `External/**`, build output, test result output, and release artifacts are excluded. [Directory.Build.props](../Directory.Build.props) also sets `SonarQubeExclude` for the referenced `SonicAudioLib` and `VGAudio` projects, so their sources are not analyzed during compilation.

Coverage is excluded for every file with `sonar.coverage.exclusions=**`. The workflow does not collect or upload coverage reports. Tests continue to run in the ordinary CI workflow.

The scanner waits up to ten minutes for SonarQube Cloud's quality gate result and fails the job when the gate fails. Review findings in SonarQube Cloud, fix valid issues, and rerun the pull request checks. For a confirmed false positive, record the specific reasoning with the issue resolution; do not disable unrelated rules or exclude owned source files to clear the gate.

The scanner version and action revisions are pinned in the workflow. See the official [SonarScanner for .NET documentation](https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/scanners/dotnet/using) for scanner behavior and the [package listing](https://www.nuget.org/packages/dotnet-sonarscanner) for releases.
