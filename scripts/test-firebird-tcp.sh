#!/usr/bin/env bash
set -euo pipefail

if (( $# > 1 )); then
    echo "Usage: bash scripts/test-firebird-tcp.sh [5.0|4.0|3.0|2.5]" >&2
    exit 2
fi

runtime=${FIREBIRD_TEST_CONTAINER_RUNTIME:-podman}
if [[ $runtime != podman && $runtime != docker ]]; then
    echo "FIREBIRD_TEST_CONTAINER_RUNTIME must be podman or docker." >&2
    exit 2
fi

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
project="$repo_root/tests/FirebirdStudio.Tests/FirebirdStudio.Tests.csproj"

run_version() (
    version=$1
    database_directory=/var/lib/firebird/data
    password_variable=FIREBIRD_ROOT_PASSWORD
    case "$version" in
        5.0) image=docker.io/firebirdsql/firebird:5.0.4 ;;
        4.0) image=docker.io/firebirdsql/firebird:4.0.7 ;;
        3.0) image=docker.io/firebirdsql/firebird:3.0.14 ;;
        2.5)
            image=docker.io/jacobalberty/firebird:2.5-sc
            database_directory=/firebird/data
            password_variable=ISC_PASSWORD
            ;;
        *) echo "Unsupported Firebird version: $version" >&2; exit 2 ;;
    esac

    echo "== Firebird $version ($image) =="
    password=$(od -An -N4 -tx1 /dev/urandom | tr -d '[:space:]')
    container=$(env "$password_variable=$password" "$runtime" run --detach \
        --publish 127.0.0.1::3050 --env "$password_variable" "$image")
    cleanup() {
        status=$?
        if (( status != 0 )); then
            "$runtime" logs --tail 30 "$container" >&2 || true
        fi
        "$runtime" rm --force --volumes "$container" >/dev/null || exit 1
    }
    trap cleanup EXIT

    endpoint=$("$runtime" port "$container" 3050/tcp)
    port=${endpoint##*:}
    deadline=$((SECONDS + 60))
    until "$runtime" exec "$container" bash -c ': >/dev/tcp/127.0.0.1/3050' >/dev/null 2>&1; do
        if (( SECONDS >= deadline )); then
            echo "Firebird $version did not start listening within 60 seconds." >&2
            exit 1
        fi
        sleep 1
    done

    FIREBIRD_TEST_VERSION="$version" \
        FIREBIRD_TEST_HOST=127.0.0.1 FIREBIRD_TEST_PORT="$port" FIREBIRD_TEST_USER=SYSDBA \
        FIREBIRD_TEST_DIRECTORY="$database_directory" FIREBIRD_TEST_PASSWORD="$password" \
        dotnet test "$project" -c Release --no-build --no-restore \
            --filter 'Category=FirebirdIntegration' --logger 'console;verbosity=detailed'
)

if (( $# == 1 )); then
    versions=("$1")
else
    versions=(5.0 4.0 3.0 2.5)
fi

dotnet build "$project" -c Release
for version in "${versions[@]}"; do
    run_version "$version"
done
