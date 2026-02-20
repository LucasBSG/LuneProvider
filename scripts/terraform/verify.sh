#!/usr/bin/env bash
set -euo pipefail

TERRAFORM_BINARY_PATH="${TERRAFORM_BINARY_PATH:-terraform}"
TERRAFORM_REQUIRED_VERSION="${TERRAFORM_REQUIRED_VERSION:-1.8.0}"

normalize_version() {
  echo "$1" | sed -E 's/^v//' | sed -E 's/-.*$//'
}

version_ge() {
  local lhs rhs
  lhs="$(normalize_version "$1")"
  rhs="$(normalize_version "$2")"

  local lhs_a lhs_b lhs_c rhs_a rhs_b rhs_c
  IFS='.' read -r lhs_a lhs_b lhs_c <<<"${lhs}"
  IFS='.' read -r rhs_a rhs_b rhs_c <<<"${rhs}"

  lhs_a="${lhs_a:-0}"
  lhs_b="${lhs_b:-0}"
  lhs_c="${lhs_c:-0}"
  rhs_a="${rhs_a:-0}"
  rhs_b="${rhs_b:-0}"
  rhs_c="${rhs_c:-0}"

  if ((10#${lhs_a} > 10#${rhs_a})); then return 0; fi
  if ((10#${lhs_a} < 10#${rhs_a})); then return 1; fi
  if ((10#${lhs_b} > 10#${rhs_b})); then return 0; fi
  if ((10#${lhs_b} < 10#${rhs_b})); then return 1; fi
  if ((10#${lhs_c} >= 10#${rhs_c})); then return 0; fi
  return 1
}

if ! command -v "${TERRAFORM_BINARY_PATH}" >/dev/null 2>&1; then
  echo "error: terraform binary '${TERRAFORM_BINARY_PATH}' not found" >&2
  exit 1
fi

detected_version="$("${TERRAFORM_BINARY_PATH}" version -json 2>/dev/null | tr -d '\n' | sed -n 's/.*"terraform_version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
if [ -z "${detected_version}" ]; then
  detected_version="$("${TERRAFORM_BINARY_PATH}" version | head -n 1 | sed -n 's/^Terraform v\([0-9][^ ]*\).*/\1/p')"
fi

if [ -z "${detected_version}" ]; then
  echo "error: unable to detect terraform version from '${TERRAFORM_BINARY_PATH}'" >&2
  exit 1
fi

if ! version_ge "${detected_version}" "${TERRAFORM_REQUIRED_VERSION}"; then
  echo "error: terraform version ${detected_version} is lower than required ${TERRAFORM_REQUIRED_VERSION}" >&2
  exit 1
fi

echo "Terraform binary OK: ${TERRAFORM_BINARY_PATH} (${detected_version})"
