#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DESTINATION="${1:-${SCRIPT_DIR}/../../../App_Data/media-models}"
mkdir -p "${DESTINATION}"

download_and_verify() {
  local name="$1" file="$2" url="$3" expected="$4"
  local target="${DESTINATION}/${file}" temporary="${DESTINATION}/.${file}.download"
  echo "Downloading ${name}..."
  curl --fail --location --retry 3 --output "${temporary}" "${url}"
  local actual
  actual="$(sha256sum "${temporary}" | awk '{print $1}')"
  if [[ "${actual}" != "${expected}" ]]; then
    rm -f "${temporary}"
    echo "Checksum mismatch for ${name}. Expected ${expected}; received ${actual}." >&2
    exit 1
  fi
  mv -f "${temporary}" "${target}"
  echo "Verified ${file}"
}

download_and_verify \
  "YuNet 2026may" \
  "face_detection_yunet_2026may.onnx" \
  "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx" \
  "ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0"

download_and_verify \
  "SFace 2021dec" \
  "face_recognition_sface_2021dec.onnx" \
  "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx" \
  "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79"

echo "Approved models installed at ${DESTINATION}"
echo "Review model licences and organisational approval before enabling MediaLibrary:People."
