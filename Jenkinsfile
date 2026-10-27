// The safety net of SchulerPark, written by aqaa from what its QA run found: unit tests with coverage and its
// floor, integration tests against the databases they need, end-to-end tests, and lint, SAST and dependency
// checks (report only: a finding marks the stage unstable until it is triaged).
// Needs the plugins Pipeline, Docker Pipeline, JUnit, Coverage, Timestamper and Workspace Cleanup;
// integration stages run on an agent labelled 'docker'.
pipeline {
  agent none
  options {
    timeout(time: 90, unit: 'MINUTES')
    timestamps()
    buildDiscarder(logRotator(numToKeepStr: '30', artifactNumToKeepStr: '10'))
    disableConcurrentBuilds(abortPrevious: true)
  }
  environment {
    CI = 'true'
  }
  stages {
    stage('Tests') {
      parallel {
        stage('Unit: dotnet backend') {
          agent { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0'; args '-v /var/run/docker.sock:/var/run/docker.sock' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('backend') {
              sh '''
                set -eu
                export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                mkdir -p test-reports
                dotnet restore
                dotnet tool update --global trx2junit --version 2.1.0
                if grep -rqsi --include='*.csproj' --include='*.props' --include='*.targets' coverlet.collector .; then c="XPlat Code Coverage"; else c="Code Coverage;Format=cobertura"; fi; dotnet test --no-restore --filter "Category!=Integration&Category!=E2E" --logger "trx;LogFilePrefix=unit" --results-directory test-reports --collect "$c" && trx2junit test-reports/*.trx
              '''
            }
          }
          post {
            always {
              junit allowEmptyResults: true, testResults: 'backend/test-reports/*.xml'
              recordCoverage id: 'coverage-dotnet-backend', name: 'Coverage dotnet backend', tools: [[parser: 'COBERTURA', pattern: 'backend/test-reports/**/*.cobertura.xml']], sourceDirectories: [[path: 'backend']], qualityGates: [[threshold: 17.0, metric: 'LINE', baseline: 'PROJECT', criticality: 'FAILURE']]
              archiveArtifacts allowEmptyArchive: true, artifacts: 'backend/test-reports/**'
            }
            cleanup {
              cleanWs deleteDirs: true, notFailBuild: true
            }
          }
        }
        stage('Integration: dotnet backend') {
          agent { label 'docker' }
          options { timeout(time: 45, unit: 'MINUTES') }
          steps {
            script {
              docker.image('postgres:16-alpine').withRun('-e POSTGRES_USER=aqaa -e POSTGRES_PASSWORD=aqaa -e POSTGRES_DB=aqaa') { postgres ->
                docker.image('postgres:16-alpine').inside("--link ${postgres.id}:postgres") {
                  sh 'until pg_isready -h postgres -U aqaa -d aqaa; do sleep 1; done'
                }
                docker.image('mcr.microsoft.com/dotnet/sdk:10.0').inside("--link ${postgres.id}:postgres -e 'AQAA_PG_URL=postgres://aqaa:aqaa@postgres:5432/aqaa?sslmode=disable' -e 'AQAA_PG_CONNECTION_STRING=Host=postgres;Port=5432;Database=aqaa;Username=aqaa;Password=aqaa' -e 'DATABASE_URL=postgres://aqaa:aqaa@postgres:5432/aqaa?sslmode=disable' -e 'AQAA_PG_JDBC_URL=jdbc:postgresql://postgres:5432/aqaa?user=aqaa&password=aqaa' -v /var/run/docker.sock:/var/run/docker.sock") {
                  dir('backend') {
                    sh '''
                      set -eu
                      export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                      mkdir -p test-reports
                      dotnet restore
                      dotnet tool update --global trx2junit --version 2.1.0
                      dotnet test --no-restore --filter "Category=Integration" --logger "trx;LogFilePrefix=integration" --results-directory test-reports && trx2junit test-reports/*.trx
                    '''
                  }
                }
              }
            }
          }
          post {
            always {
              junit allowEmptyResults: true, testResults: 'backend/test-reports/*.xml'
              archiveArtifacts allowEmptyArchive: true, artifacts: 'backend/test-reports/**'
            }
            cleanup {
              cleanWs deleteDirs: true, notFailBuild: true
            }
          }
        }
        stage('End-to-end: node e2e') {
          agent { docker { image 'mcr.microsoft.com/playwright:v1.56.1-noble' } }
          options { timeout(time: 45, unit: 'MINUTES') }
          steps {
            dir('e2e') {
              sh '''
                set -eu
                mkdir -p test-reports
                npm ci
                npx playwright install chromium
                PLAYWRIGHT_JUNIT_OUTPUT_NAME=test-reports/e2e.xml npx playwright test --reporter=junit,list
              '''
            }
          }
          post {
            always {
              junit allowEmptyResults: true, testResults: 'e2e/test-reports/*.xml'
              archiveArtifacts allowEmptyArchive: true, artifacts: 'e2e/test-reports/**'
            }
            cleanup {
              cleanWs deleteDirs: true, notFailBuild: true
            }
          }
        }
        stage('Unit: shell') {
          agent { dockerfile { filename 'shell.Dockerfile'; dir 'ci/jenkins'; args '-v /var/run/docker.sock:/var/run/docker.sock' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('.') {
              sh '''
                set -eu
                export BATS_LIB_PATH="${BATS_LIB_PATH:-/usr/lib/bats}"
                mkdir -p test-reports
                T=""; for d in test tests; do if [ -d "$d" ]; then T="$T $d"; fi; done
                if [ -z "$T" ]; then echo "no bats tests"; exit 0; fi
                # shellcheck disable=SC2086
                bats --recursive --allow-empty-suite --formatter tap --report-formatter junit --output test-reports --filter-tags '!integration,!e2e' $T
              '''
            }
          }
          post {
            always {
              junit allowEmptyResults: true, testResults: 'test-reports/*.xml'
              archiveArtifacts allowEmptyArchive: true, artifacts: 'test-reports/**'
            }
            cleanup {
              cleanWs deleteDirs: true, notFailBuild: true
            }
          }
        }
        stage('Unit: typescript frontend') {
          agent { docker { image 'node:22-bookworm'; args '-v /var/run/docker.sock:/var/run/docker.sock' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('frontend') {
              sh '''
                set -eu
                mkdir -p test-reports
                npm ci
                npm install --no-save --no-audit --no-fund "@vitest/coverage-v8@$(node -p "require('vitest/package.json').version")"
                npx vitest run --coverage --coverage.reporter=cobertura --coverage.reportsDirectory=test-reports/coverage --reporter=default --reporter=junit --outputFile.junit=test-reports/unit.xml --exclude '**/*.integration.test.*' --exclude 'e2e/**'
              '''
            }
          }
          post {
            always {
              junit allowEmptyResults: true, testResults: 'frontend/test-reports/*.xml'
              recordCoverage id: 'coverage-typescript-frontend', name: 'Coverage typescript frontend', tools: [[parser: 'COBERTURA', pattern: 'frontend/test-reports/coverage/cobertura-coverage.xml']], sourceDirectories: [[path: 'frontend']], qualityGates: [[threshold: 61.0, metric: 'LINE', baseline: 'PROJECT', criticality: 'FAILURE']]
              archiveArtifacts allowEmptyArchive: true, artifacts: 'frontend/test-reports/**'
            }
            cleanup {
              cleanWs deleteDirs: true, notFailBuild: true
            }
          }
        }
      }
    }
    stage('Checks') {
      parallel {
        stage('Checks: docker') {
          agent { dockerfile { filename 'docker.Dockerfile'; dir 'ci/jenkins' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('.') {
              sh '''
                set -eu
                mkdir -p test-reports
              '''
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'hadolint found issues') {
                sh '''
                  set -eu
                  find . -type f \\( -name Dockerfile -o -name 'Dockerfile.*' -o -name '*.Dockerfile' -o -name Containerfile \\) -not -path './.git/*' -not -path '*/node_modules/*' -print0 | xargs -0 -r hadolint
                '''
              }
            }
          }
          post {
            always { archiveArtifacts allowEmptyArchive: true, artifacts: 'test-reports/**' }
            cleanup { cleanWs deleteDirs: true, notFailBuild: true }
          }
        }
        stage('Checks: dotnet backend') {
          agent { docker { image 'mcr.microsoft.com/dotnet/sdk:10.0' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('backend') {
              sh '''
                set -eu
                export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                mkdir -p test-reports
                dotnet restore
                dotnet tool update --global trx2junit --version 2.1.0
              '''
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'dotnet format found issues') {
                sh '''
                  set -eu
                  export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                  dotnet format --verify-no-changes --no-restore
                '''
              }
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'roslyn security analyzers found issues') {
                sh '''
                  set -eu
                  export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                  mkdir -p test-reports/roslyn
                  cat > "$RUNNER_TEMP_DIR/aqaa-sast.props" <<EOF
                  <Project><PropertyGroup><RunAnalyzers>true</RunAnalyzers><AnalysisModeSecurity>All</AnalysisModeSecurity><ErrorLog>$PWD/test-reports/roslyn/\\$(MSBuildProjectName).sarif,version=2.1</ErrorLog></PropertyGroup></Project>
                  EOF
                  dotnet build --no-restore -nologo -p:CustomBeforeMicrosoftCommonProps="$RUNNER_TEMP_DIR/aqaa-sast.props" -p:TreatWarningsAsErrors=false
                '''
              }
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'vulnerable packages found issues') {
                sh '''
                  set -eu
                  export PATH="$PATH:$HOME/.dotnet/tools" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 RUNNER_TEMP_DIR="${RUNNER_TEMP:-${WORKSPACE_TMP:-/tmp}}"
                  dotnet list package --vulnerable --include-transitive | tee test-reports/vulnerable.txt && ! grep -q "has the following vulnerable packages" test-reports/vulnerable.txt
                '''
              }
            }
          }
          post {
            always { archiveArtifacts allowEmptyArchive: true, artifacts: 'backend/test-reports/**' }
            cleanup { cleanWs deleteDirs: true, notFailBuild: true }
          }
        }
        stage('Checks: node e2e') {
          agent { docker { image 'node:22-bookworm' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('e2e') {
              sh '''
                set -eu
                mkdir -p test-reports
                npm ci
              '''
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'eslint found issues') {
                sh '''
                  set -eu
                  if ls eslint.config.* .eslintrc* >/dev/null 2>&1; then npx eslint .; else echo 'no ESLint configuration'; fi
                '''
              }
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'npm audit found issues') {
                sh '''
                  set -eu
                  npm audit --audit-level=high --omit=dev
                '''
              }
            }
          }
          post {
            always { archiveArtifacts allowEmptyArchive: true, artifacts: 'e2e/test-reports/**' }
            cleanup { cleanWs deleteDirs: true, notFailBuild: true }
          }
        }
        stage('Checks: shell') {
          agent { dockerfile { filename 'shell.Dockerfile'; dir 'ci/jenkins' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('.') {
              sh '''
                set -eu
                export BATS_LIB_PATH="${BATS_LIB_PATH:-/usr/lib/bats}"
                mkdir -p test-reports
              '''
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'shellcheck found issues') {
                sh '''
                  set -eu
                  export BATS_LIB_PATH="${BATS_LIB_PATH:-/usr/lib/bats}"
                  find . -type f \\( -name '*.sh' -o -name '*.bash' \\) -not -path './.git/*' -not -path '*/node_modules/*' -print0 | xargs -0 -r shellcheck -x -P SCRIPTDIR
                '''
              }
            }
          }
          post {
            always { archiveArtifacts allowEmptyArchive: true, artifacts: 'test-reports/**' }
            cleanup { cleanWs deleteDirs: true, notFailBuild: true }
          }
        }
        stage('Checks: typescript frontend') {
          agent { docker { image 'node:22-bookworm' } }
          options { timeout(time: 30, unit: 'MINUTES') }
          steps {
            dir('frontend') {
              sh '''
                set -eu
                mkdir -p test-reports
                npm ci
                npm install --no-save --no-audit --no-fund "@vitest/coverage-v8@$(node -p "require('vitest/package.json').version")"
              '''
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'eslint found issues') {
                sh '''
                  set -eu
                  if ls eslint.config.* .eslintrc* >/dev/null 2>&1; then npx eslint .; else echo 'no ESLint configuration'; fi
                '''
              }
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'tsc found issues') {
                sh '''
                  set -eu
                  npx tsc --noEmit
                '''
              }
              catchError(buildResult: 'SUCCESS', stageResult: 'UNSTABLE', message: 'npm audit found issues') {
                sh '''
                  set -eu
                  npm audit --audit-level=high --omit=dev
                '''
              }
            }
          }
          post {
            always { archiveArtifacts allowEmptyArchive: true, artifacts: 'frontend/test-reports/**' }
            cleanup { cleanWs deleteDirs: true, notFailBuild: true }
          }
        }
      }
    }
  }
}
