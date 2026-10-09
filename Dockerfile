FROM zmkfirmware/zmk-build-arm:stable

USER root
WORKDIR /work

# Cache the toolchain sources independently of keymap/configuration edits.
COPY config/west.yml /work/config/west.yml
RUN west init -l /work/config \
    && west update --fetch-opt=--filter=tree:0 \
    && west zephyr-export

COPY tools/docker-build.py /usr/local/bin/build-firmware.py
ENTRYPOINT ["python3", "/usr/local/bin/build-firmware.py"]
CMD ["corne_left"]
