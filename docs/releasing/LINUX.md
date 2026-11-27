# Linux x64 portable package

Build on Linux with Bash, tar, gzip, and the .NET 10 SDK. Run from the repository root:

```bash
bash scripts/publish-linux.sh
```

The script runs the default test suite, publishes the application and its bundled updater, and writes `releases/FirebirdStudio-<version>-linux-x64-self-contained.tar.gz`. It reads the version from `VERSION`.

## Test on Fedora

The package includes the .NET runtime. The target needs a Linux x86-64 graphical desktop with X11 or XWayland and the native .NET and Avalonia dependencies. On Fedora, install any missing packages:

```bash
sudo dnf install glibc libgcc ca-certificates openssl-libs libstdc++ libicu tzdata krb5-libs \
  libX11 libICE libSM fontconfig
```

See the [.NET Fedora dependency list](https://learn.microsoft.com/dotnet/core/install/linux-fedora) and [Avalonia Linux requirements](https://docs.avaloniaui.net/docs/supported-platforms).

Copy the archive to the test machine and extract it to a directory owned by your user. Substitute the version from the filename:

```bash
version=0.2.2
package="FirebirdStudio-$version-linux-x64-self-contained"
tar -xzf "$package.tar.gz"
cd "$package"
./FirebirdStudio.Desktop
```

### Test a Firebird connection

Use a disposable server. To start Firebird 5 with Podman on the Fedora test machine, enter a temporary password when prompted:

```bash
read -rsp 'Temporary SYSDBA password: ' FIREBIRD_ROOT_PASSWORD
printf '\n'
export FIREBIRD_ROOT_PASSWORD
container=$(podman run --rm --detach --publish 127.0.0.1::3050 \
  --env FIREBIRD_ROOT_PASSWORD --env FIREBIRD_DATABASE=smoke.fdb \
  docker.io/firebirdsql/firebird:5.0.4)
unset FIREBIRD_ROOT_PASSWORD
podman port "$container" 3050/tcp
```

In Firebird Studio, use host `127.0.0.1`, the displayed port, user `SYSDBA`, the temporary password, and database `/var/lib/firebird/data/smoke.fdb`. Connect and use **File → Create database** on the active instance to create `/var/lib/firebird/data/studio-smoke.fdb`. Reopen the new database, create a test table, find it in **Database Objects**, and run a query in the SQL editor. Record the Fedora version, desktop session, Firebird version, and any missing native dependencies. [Firebird container configuration](https://hub.docker.com/r/firebirdsql/firebird)

Disconnect and stop the test container with `podman stop "$container"`.
