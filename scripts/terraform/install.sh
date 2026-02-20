#!/usr/bin/env bash
set -euo pipefail

TERRAFORM_VERSION="${TERRAFORM_VERSION:-1.8.0}"
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"
TMP_DIR="$(mktemp -d)"
ARCHIVE=""

cleanup() {
  rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "error: required command '$1' is not available" >&2
    exit 1
  fi
}

detect_os() {
  case "$(uname -s | tr '[:upper:]' '[:lower:]')" in
    linux) echo "linux" ;;
    darwin) echo "darwin" ;;
    *)
      echo "error: unsupported OS '$(uname -s)'" >&2
      exit 1
      ;;
  esac
}

detect_arch() {
  case "$(uname -m)" in
    x86_64|amd64) echo "amd64" ;;
    arm64|aarch64) echo "arm64" ;;
    *)
      echo "error: unsupported architecture '$(uname -m)'" >&2
      exit 1
      ;;
  esac
}

require_cmd curl
require_cmd unzip
require_cmd install

os="$(detect_os)"
arch="$(detect_arch)"
ARCHIVE="terraform_${TERRAFORM_VERSION}_${os}_${arch}.zip"
url="https://releases.hashicorp.com/terraform/${TERRAFORM_VERSION}/${ARCHIVE}"
zip_path="${TMP_DIR}/${ARCHIVE}"

echo "Downloading Terraform ${TERRAFORM_VERSION} (${os}/${arch})..."
curl -fsSL "${url}" -o "${zip_path}"
unzip -q "${zip_path}" -d "${TMP_DIR}"

mkdir -p "${INSTALL_DIR}"
install -m 0755 "${TMP_DIR}/terraform" "${INSTALL_DIR}/terraform"

echo "Terraform installed at ${INSTALL_DIR}/terraform"
"${INSTALL_DIR}/terraform" version | head -n 1
