# The command chromadotnet without .NET: the native file for linux-musl-x64 on Alpine, which has the CA certificates
# for HTTPS. The build context is a folder with that file, named chromadotnet.
FROM alpine:3.24@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf

LABEL org.opencontainers.image.source="https://github.com/ChromaDotNet/ChromaDB.Client"
LABEL org.opencontainers.image.description="The command chromadotnet: backup, restore and verify of Chroma collections through the API."
LABEL org.opencontainers.image.licenses="MIT"

COPY --chmod=755 chromadotnet /usr/local/bin/chromadotnet

# The backups are read and written in the working folder, which a volume can mount; --user "$(id -u):$(id -g)" writes
# them with the owner of that folder.
WORKDIR /data
ENTRYPOINT ["chromadotnet"]
