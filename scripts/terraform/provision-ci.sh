#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOOLS_DIR="${ROOT_DIR}/.tools/bin"

mkdir -p "${TOOLS_DIR}"
TERRAFORM_VERSION="${TERRAFORM_VERSION:-1.8.0}" INSTALL_DIR="${TOOLS_DIR}" "${ROOT_DIR}/scripts/terraform/install.sh"
TERRAFORM_BINARY_PATH="${TOOLS_DIR}/terraform" TERRAFORM_REQUIRED_VERSION="${TERRAFORM_REQUIRED_VERSION:-1.8.0}" "${ROOT_DIR}/scripts/terraform/verify.sh"

if [ -n "${GITHUB_PATH:-}" ]; then
  echo "${TOOLS_DIR}" >> "${GITHUB_PATH}"
fi

echo "Terraform CLI provisioned for CI at ${TOOLS_DIR}/terraform"
