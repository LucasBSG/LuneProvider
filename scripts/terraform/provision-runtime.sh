#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

: "${INSTALL_DIR:=/usr/local/bin}"
: "${TERRAFORM_VERSION:=1.8.0}"
: "${TERRAFORM_REQUIRED_VERSION:=1.8.0}"

TERRAFORM_VERSION="${TERRAFORM_VERSION}" INSTALL_DIR="${INSTALL_DIR}" "${ROOT_DIR}/scripts/terraform/install.sh"
TERRAFORM_BINARY_PATH="${INSTALL_DIR}/terraform" TERRAFORM_REQUIRED_VERSION="${TERRAFORM_REQUIRED_VERSION}" "${ROOT_DIR}/scripts/terraform/verify.sh"

echo "Terraform CLI provisioned for runtime at ${INSTALL_DIR}/terraform"
