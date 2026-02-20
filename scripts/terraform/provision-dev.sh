#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOCAL_BIN_DIR="${HOME}/.local/bin"

mkdir -p "${LOCAL_BIN_DIR}"
TERRAFORM_VERSION="${TERRAFORM_VERSION:-1.8.0}" INSTALL_DIR="${LOCAL_BIN_DIR}" "${ROOT_DIR}/scripts/terraform/install.sh"
TERRAFORM_BINARY_PATH="${LOCAL_BIN_DIR}/terraform" TERRAFORM_REQUIRED_VERSION="${TERRAFORM_REQUIRED_VERSION:-1.8.0}" "${ROOT_DIR}/scripts/terraform/verify.sh"

echo "Terraform CLI provisioned for dev at ${LOCAL_BIN_DIR}/terraform"
echo "Add '${LOCAL_BIN_DIR}' to PATH if needed."
