# Build agent for the shell stages of the Jenkinsfile (the tool versions of aqaa's runner, checksums verified).
FROM debian:trixie-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl unzip xz-utils git bash jq && rm -rf /var/lib/apt/lists/*
SHELL ["/bin/bash", "-o", "pipefail", "-c"]
RUN T="$(mktemp -d)" \
    && cd "$T" \
    && curl -fsSL -o bats.tgz "https://github.com/bats-core/bats-core/archive/refs/tags/v1.14.0.tar.gz" && echo "bb537b70b15b732f6d8827dd6578e3d8ce166636ce1f18ea9a074184fcce9177  bats.tgz" | sha256sum -c - \
    && curl -fsSL -o support.tgz "https://github.com/bats-core/bats-support/archive/refs/tags/v0.3.0.tar.gz" && echo "7815237aafeb42ddcc1b8c698fc5808026d33317d8701d5ec2396e9634e2918f  support.tgz" | sha256sum -c - \
    && curl -fsSL -o assert.tgz "https://github.com/bats-core/bats-assert/archive/refs/tags/v2.2.4.tar.gz" && echo "e305df20c4c36cba4570e10f1cd824efb1ae71d88b5ef474550781ebee04192f  assert.tgz" | sha256sum -c - \
    && curl -fsSL -o file.tgz "https://github.com/bats-core/bats-file/archive/refs/tags/v0.4.0.tar.gz" && echo "9b69043241f3af1c2d251f89b4fcafa5df3f05e97b89db18d7c9bdf5731bb27a  file.tgz" | sha256sum -c - \
    && mkdir bats && tar -xzf bats.tgz --strip-components 1 -C bats && ./bats/install.sh /usr/local \
    && for l in support assert file; do mkdir -p /usr/lib/bats/bats-$l && tar -xzf $l.tgz --strip-components 1 -C /usr/lib/bats/bats-$l; done \
    && curl -fsSL -o shellcheck.txz "https://github.com/koalaman/shellcheck/releases/download/v0.11.0/shellcheck-v0.11.0.linux.x86_64.tar.xz" && echo "8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198  shellcheck.txz" | sha256sum -c - \
    && tar -xJf shellcheck.txz --strip-components 1 shellcheck-v0.11.0/shellcheck \
    && install -m 0755 shellcheck /usr/local/bin/shellcheck \
    && cd - > /dev/null \
    && rm -rf "$T"
ENV BATS_LIB_PATH=/usr/lib/bats
