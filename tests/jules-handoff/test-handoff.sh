#!/usr/bin/env bash
# Mocked Jules hand-off scenarios. No network and no git checkout required.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LIB="${ROOT}/.github/scripts/jules-handoff-lib.sh"
# shellcheck source=/dev/null
source "${LIB}"

SHA_A="1111111111111111111111111111111111111111"
SHA_B="2222222222222222222222222222222222222222"
failures=0

fail() {
  echo "FAIL: $*" >&2
  failures=$((failures + 1))
}

assert_eq() {
  local actual="$1" expected="$2" label="$3"
  if [[ "${actual}" != "${expected}" ]]; then
    fail "${label}: expected [${expected}] got [${actual}]"
  else
    echo "ok: ${label}"
  fi
}

action_of() {
  jules_audit_action "$1" | sed -n '1p'
}

target_of() {
  jules_audit_action "$1" | sed -n '2p'
}

spec() {
  jq -n \
    --arg verdict "$1" \
    --arg changed "$2" \
    --arg author "${3:-}" \
    --arg audited "${4:-$SHA_A}" \
    --arg branch "${5:-$SHA_A}" \
    --arg kind "${6:-task}" \
    --argjson count "${7:-1}" \
    --arg target_sha "${8:-$SHA_A}" \
    --arg target_number "${9:-7}" \
    --argjson draft "${10:-true}" \
    --arg fix_number "${11:-8}" \
    --arg fix_state "${12:-OPEN}" \
    '{
      verdict: $verdict,
      changed_files: $changed,
      fix_author: $author,
      fix_state: $fix_state,
      audited_sha: $audited,
      branch_sha: $branch,
      audit_kind: $kind,
      target_count: $count,
      target_sha: $target_sha,
      target_number: $target_number,
      target_is_draft: $draft,
      fix_number: $fix_number
    }'
}

echo "=== verdict classification ==="
assert_eq "$(jules_classify_verdict $'No blocking findings were found.\nJULES_AUDIT_VERDICT: clean')" clean "explicit clean"
assert_eq "$(jules_classify_verdict $'**BLOCKING FINDING**: branch lookup is wrong\nJULES_AUDIT_VERDICT: clean')" unknown "heading conflicts with clean token"
assert_eq "$(jules_classify_verdict "$(cat <<'EOF'
### Jules Audit Report

**BLOCKING FINDING**: The workflow looks for agent/jules → agent/jules.

No code changes were made.
EOF
)")" blocking "empty audit heading is blocking"
assert_eq "$(jules_classify_verdict "Jules Audit Report: No Changes (Blocking Finding)")" blocking "title marks a blocking finding"
assert_eq "$(jules_classify_verdict $'JULES_AUDIT_VERDICT: clean\nJULES_AUDIT_VERDICT: blocking')" unknown "both tokens are not approval"
assert_eq "$(jules_classify_verdict "The session completed with no file changes.")" unknown "completion text is not a verdict"
assert_eq "$(jules_is_jules_author "app/google-labs-jules" && echo yes)" yes "graphql jules author"
assert_eq "$(jules_is_jules_author "google-labs-jules[bot]" && echo yes)" yes "event jules author"
assert_eq "$(jules_is_jules_author "FunGeorgy" && echo yes || echo no)" no "human author"

echo "=== audit actions ==="
assert_eq "$(action_of "$(spec clean none "")")" ready "clean task with no pull request"
assert_eq "$(action_of "$(spec clean 0 app/google-labs-jules)")" close-empty-ready "clean empty jules pull request"
assert_eq "$(action_of "$(spec blocking 0 app/google-labs-jules)")" blocking-preserve "zero files with blocker is not approval"
assert_eq "$(target_of "$(spec blocking 0 app/google-labs-jules)")" 7 "blocker stays on the audited pull request"
assert_eq "$(action_of "$(spec unknown 0 app/google-labs-jules)")" unknown-preserve "unknown verdict stays pending"
assert_eq "$(action_of "$(spec clean 2 app/google-labs-jules)")" await-fix "clean correction waits for a human merge"
assert_eq "$(action_of "$(spec clean none "" "$SHA_A" "$SHA_B")")" stale "new commit refuses the hand-off"
assert_eq "$(action_of "$(spec clean none "" "$SHA_A" "$SHA_A" task 2)")" ambiguous-target "two task targets are ambiguous"
assert_eq "$(action_of "$(spec clean none "" "$SHA_A" "$SHA_A" integration 0 "$SHA_A" - true -)")" missing-target "integration clean without a promotion pull request"
assert_eq "$(action_of "$(spec clean none "" "$SHA_A" "$SHA_A" integration 2 "$SHA_A" 4 true -)")" ambiguous-target "two promotion pull requests"
assert_eq "$(action_of "$(spec clean none "" "$SHA_A" "$SHA_A" integration 1 "$SHA_A" 9 true -)")" ready "one promotion pull request at the audited sha"
assert_eq "$(action_of "$(spec clean 3 app/google-labs-jules "$SHA_A" "$SHA_A" integration 1 "$SHA_A" 9 true 8)")" defer-integration-fix "integration correction does not ready early"
assert_eq "$(action_of "$(spec blocking 0 "" "$SHA_A" "$SHA_A" integration 0 "" - true -)")" blocking-preserve "integration blocker without a promotion pull request"
assert_eq "$(action_of "$(spec clean 0 someone)")" unknown-preserve "empty non-jules pull request is not closed as clean"

fix_action() {
  jules_fix_ready_action "$@" | sed -n '1p'
}

task_candidate="$(jq -nc --arg sha "$SHA_A" '[{number:7,isDraft:true,headRefOid:$sha,labels:[{name:"jules:awaiting-fix"}]}]')"
task_blocking="$(jq -nc --arg sha "$SHA_A" '[{number:7,isDraft:true,headRefOid:$sha,labels:[{name:"jules:awaiting-fix"},{name:"jules:blocking"}]}]')"
task_stale="$(jq -nc --arg sha "$SHA_B" '[{number:7,isDraft:true,headRefOid:$sha,labels:[{name:"jules:awaiting-fix"}]}]')"
two_tasks="$(jq -nc --arg sha "$SHA_A" '[{number:7,isDraft:true,headRefOid:$sha,labels:[{name:"jules:awaiting-fix"}]},{number:8,isDraft:true,headRefOid:$sha,labels:[{name:"jules:awaiting-fix"}]}]')"
promotion="$(jq -nc --arg sha "$SHA_A" '[{number:9,isDraft:true,headRefOid:$sha,labels:[]}]')"
two_promotions="$(jq -nc --arg sha "$SHA_A" '[{number:9,isDraft:true,headRefOid:$sha,labels:[]},{number:10,isDraft:false,headRefOid:$sha,labels:[]}]')"

echo "=== correction mapping ==="
assert_eq "$(fix_action fix/foo "$SHA_A" none none "${task_candidate}")" ready "task correction readies the awaiting-fix pull request"
assert_eq "$(fix_action fix/foo "$SHA_A" task 7 "${two_tasks}")" ready "marker selects one task pull request"
assert_eq "$(fix_action fix/foo "$SHA_A" task 99 "${two_tasks}")" missing-target "marker for a missing pull request does not fall back"
assert_eq "$(fix_action fix/foo "$SHA_A" none none "${task_stale}")" stale "task head moved after the merge commit"
assert_eq "$(fix_action fix/foo "$SHA_A" none none "${task_blocking}")" withhold-blocking "blocking label survives the correction merge"
assert_eq "$(fix_action fix/foo "$SHA_A" none none "[]")" missing-target "no task pull request is a visible miss"
assert_eq "$(fix_action agent/jules "$SHA_A" integration none "${promotion}")" ready "integration correction targets the promotion pull request"
assert_eq "$(fix_action agent/jules "$SHA_A" integration none "[]")" missing-target "integration without a promotion pull request"
assert_eq "$(fix_action agent/jules "$SHA_A" integration none "${two_promotions}")" ambiguous-target "ambiguous promotion pull requests"
assert_eq "$(fix_action agent/jules "$SHA_A" task none "${promotion}")" marker-conflict "task marker cannot advance an integration correction"
assert_eq "$(fix_action main "$SHA_A" none none "[]")" refuse-protected "protected base is refused"
assert_eq "$(fix_action staging "$SHA_A" none none "${promotion}")" refuse-protected "staging base is refused"

marker="$(jules_marker_body task 7 "$SHA_A" "8648" clean)"
parsed="$(jules_parse_marker "${marker}")"
assert_eq "$(printf '%s\n' "${parsed}" | sed -n '1p')" task "marker kind round trip"
assert_eq "$(printf '%s\n' "${parsed}" | sed -n '2p')" 7 "marker pull request round trip"
assert_eq "$(jules_parse_marker "no marker here" | sed -n '1p')" none "absent marker"
conflict="$(printf '%s\n%s\n' "$(jules_marker_body task 7 "$SHA_A" s clean)" "$(jules_marker_body integration none "$SHA_A" s clean)")"
assert_eq "$(jules_parse_marker "${conflict}" | sed -n '1p')" conflict "disagreeing markers"

echo "=== mocked gh scenarios ==="

run_publish() {
  local log="$1"
  shift
  local summary
  summary="$(mktemp)"
  local mark
  mark="$(mktemp)"
  : > "${mark}"
  (
    set -euo pipefail
    export GH_REPO="RamnD/UGS_SDK"
    export STARTING_BRANCH="${STARTING_BRANCH:-fix/foo}"
    export BASE_BRANCH="${BASE_BRANCH:-agent/jules}"
    export PR_NUMBER="${PR_NUMBER-7}"
    export AUDITED_SHA="${AUDITED_SHA:-$SHA_A}"
    export STATE="${STATE:-COMPLETED}"
    export SESSION_URL="https://jules.example/session"
    export SESSION_ID="123"
    export FIX_PULL_REQUEST="${FIX_PULL_REQUEST:-}"
    export JULES_VERDICT_FILE="${JULES_VERDICT_FILE}"
    export JULES_MARK_OK_FILE="${mark}"
    export JULES_ACTIVITY_FETCH_FAILED="${JULES_ACTIVITY_FETCH_FAILED:-/nonexistent}"
    export GITHUB_STEP_SUMMARY="${summary}"
    export MOCK_BRANCH_SHA="${MOCK_BRANCH_SHA:-$SHA_A}"
    export MOCK_PR_JSON="${MOCK_PR_JSON}"
    export MOCK_FIX_JSON="${MOCK_FIX_JSON:-}"
    export MOCK_LABELS="${MOCK_LABELS:-}"
    export MOCK_PROMOTION_LIST="${MOCK_PROMOTION_LIST:-[]}"
    export MOCK_TASK_LIST="${MOCK_TASK_LIST:-[]}"
    export GH_LOG="${log}"
    # shellcheck source=/dev/null
    source "${LIB}"
    gh() {
      printf '%q ' "$@" | tr '\n' ' ' >> "${GH_LOG}"
      printf '\n' >> "${GH_LOG}"
      local joined="$*"
      case "$1 $2" in
        "api --silent")
          return 0
          ;;
        "api --paginate")
          printf '%s\n' ""
          ;;
        api\ repos/*)
          printf '%s\n' "${MOCK_BRANCH_SHA}"
          ;;
        "pr view")
          case "${joined}" in
            *author,changedFiles,title,body,state*)
              printf '%s\n' "${MOCK_FIX_JSON}"
              ;;
            *number,isDraft,headRefOid,headRefName,baseRefName*)
              printf '%s\n' "${MOCK_PR_JSON}"
              ;;
            *--json\ labels*)
              printf '%s\n' "${MOCK_LABELS}"
              ;;
            *)
              echo "unexpected pr view: ${joined}" >&2
              return 1
              ;;
          esac
          ;;
        "pr list")
          case "${joined}" in
            *"--head agent/jules --base staging"*)
              printf '%s\n' "${MOCK_PROMOTION_LIST}"
              ;;
            *"--base agent/jules"*)
              printf '%s\n' "${MOCK_TASK_LIST}"
              ;;
            *)
              echo "unexpected pr list: ${joined}" >&2
              return 1
              ;;
          esac
          ;;
        "label create"|"pr comment"|"pr edit"|"pr ready"|"pr close")
          return 0
          ;;
        *)
          echo "unexpected gh call: ${joined}" >&2
          return 1
          ;;
      esac
    }
    jules_publish_and_advance
  )
}

assert_repo_context() {
  local log="$1" label="$2"
  local line
  if [[ ! -s "${log}" ]]; then
    fail "${label}: gh was not called"
    return
  fi
  while IFS= read -r line; do
    case "${line}" in
      api\ --silent\ repos/RamnD/UGS_SDK/*|api\ repos/RamnD/UGS_SDK/*|api\ --paginate\ repos/RamnD/UGS_SDK/*)
        ;;
      *--repo\ RamnD/UGS_SDK*)
        ;;
      *)
        fail "${label}: gh call missing repository context: ${line}"
        return
        ;;
    esac
    case "${line}" in
      *pr\ create*|*pr\ merge*|*" --head agent/jules --base agent/jules"*)
        fail "${label}: forbidden gh call: ${line}"
        return
        ;;
    esac
  done <"${log}"
  echo "ok: ${label} repository context"
}

assert_log_lacks() {
  local log="$1" needle="$2" label="$3"
  if grep -F -q -- "${needle}" "${log}"; then
    fail "${label}: log contained [${needle}]"
  else
    echo "ok: ${label}"
  fi
}

assert_log_has() {
  local log="$1" needle="$2" label="$3"
  if grep -F -q -- "${needle}" "${log}"; then
    echo "ok: ${label}"
  else
    fail "${label}: log missing [${needle}]"
  fi
}

tmpdir="$(mktemp -d)"
trap 'rm -rf "${tmpdir}"' EXIT

# Repository context and clean task hand-off.
verdict_file="${tmpdir}/verdict-clean.txt"
printf '%s\n' "JULES_AUDIT_VERDICT: clean" > "${verdict_file}"
log="${tmpdir}/clean.log"
: > "${log}"
export JULES_VERDICT_FILE="${verdict_file}"
export MOCK_PR_JSON="{\"number\":7,\"isDraft\":true,\"headRefOid\":\"${SHA_A}\",\"headRefName\":\"fix/foo\",\"baseRefName\":\"agent/jules\"}"
export STARTING_BRANCH="fix/foo" BASE_BRANCH="agent/jules" PR_NUMBER="7" FIX_PULL_REQUEST="" MOCK_BRANCH_SHA="${SHA_A}"
set +e
run_publish "${log}"
rc=$?
set -e
assert_eq "${rc}" 0 "clean task hand-off exits 0"
assert_repo_context "${log}" "clean task"
assert_log_has "${log}" "pr ready 7 --repo RamnD/UGS_SDK" "clean task marks the draft ready"
assert_log_lacks "${log}" "pr close" "clean task does not close a pull request"

# Empty correction with a blocking finding. Do not close and do not ready.
verdict_file="${tmpdir}/verdict-block.txt"
cat > "${verdict_file}" <<'EOF'
Jules Audit Report: No Changes (Blocking Finding)
**BLOCKING FINDING**: TASK_BRANCH becomes agent/jules.
EOF
log="${tmpdir}/block.log"
: > "${log}"
export JULES_VERDICT_FILE="${verdict_file}"
export FIX_PULL_REQUEST="https://github.com/RamnD/UGS_SDK/pull/6"
export MOCK_FIX_JSON='{"author":{"login":"app/google-labs-jules"},"changedFiles":0,"title":"Jules Audit Report: No Changes (Blocking Finding)","body":"**BLOCKING FINDING**: broken lookup","state":"OPEN"}'
set +e
run_publish "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "empty blocker exits 1"
assert_repo_context "${log}" "empty blocker"
assert_log_lacks "${log}" "pr ready" "empty blocker does not mark ready"
assert_log_lacks "${log}" "pr close" "empty blocker does not close the finding"
assert_log_has "${log}" "--add-label jules:blocking" "empty blocker is labeled"

# Stale SHA after a new commit.
verdict_file="${tmpdir}/verdict-stale.txt"
printf '%s\n' "JULES_AUDIT_VERDICT: clean" > "${verdict_file}"
log="${tmpdir}/stale.log"
: > "${log}"
export JULES_VERDICT_FILE="${verdict_file}" FIX_PULL_REQUEST="" MOCK_BRANCH_SHA="${SHA_B}"
set +e
run_publish "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "stale sha exits 1"
assert_log_lacks "${log}" "pr ready" "stale sha does not mark ready"
assert_log_lacks "${log}" "pr close" "stale sha does not close a pull request"
assert_repo_context "${log}" "stale sha"

# Ambiguous task lookup.
log="${tmpdir}/ambiguous.log"
: > "${log}"
export MOCK_BRANCH_SHA="${SHA_A}" PR_NUMBER="" STARTING_BRANCH="fix/foo"
export MOCK_TASK_LIST="$(jq -nc --arg sha "$SHA_A" '[{number:7,isDraft:true,headRefOid:$sha,headRefName:"fix/foo",baseRefName:"agent/jules"},{number:11,isDraft:true,headRefOid:$sha,headRefName:"fix/foo",baseRefName:"agent/jules"}]')"
# Mark file is irrelevant when PR_NUMBER is empty; run_publish still creates one.
set +e
run_publish "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "ambiguous task target exits 1"
assert_log_lacks "${log}" "pr ready" "ambiguous task target does not mark ready"
assert_repo_context "${log}" "ambiguous task"

# Integration correction hand-off and the rejected self-lookup.
run_fix() {
  local log="$1"
  (
    set -euo pipefail
    export GH_REPO="RamnD/UGS_SDK"
    export CORRECTION_BASE CORRECTION_NUMBER MERGE_SHA
    export MOCK_BODY MOCK_COMMENTS MOCK_LIST GH_LOG="${log}"
    # shellcheck source=/dev/null
    source "${LIB}"
    gh() {
      printf '%q ' "$@" | tr '\n' ' ' >> "${GH_LOG}"
      printf '\n' >> "${GH_LOG}"
      local joined="$*"
      case "$1 $2" in
        "pr view")
          case "${joined}" in
            *--json\ body*)
              printf '%s\n' "${MOCK_BODY}"
              ;;
            *--json\ labels*)
              printf '%s\n' ""
              ;;
            *)
              echo "unexpected pr view: ${joined}" >&2
              return 1
              ;;
          esac
          ;;
        "api --paginate")
          printf '%s\n' "${MOCK_COMMENTS}"
          ;;
        "pr list")
          printf '%s\n' "${MOCK_LIST}"
          ;;
        "label create"|"pr comment"|"pr edit"|"pr ready"|"pr close")
          return 0
          ;;
        *)
          echo "unexpected gh call: ${joined}" >&2
          return 1
          ;;
      esac
    }
    jules_fix_ready_main
  )
}

log="${tmpdir}/task-fix.log"
: > "${log}"
export CORRECTION_BASE="fix/foo" CORRECTION_NUMBER="8" MERGE_SHA="${SHA_A}"
export MOCK_BODY="$(jules_marker_body task 7 "${SHA_A}" 123 clean)"
export MOCK_COMMENTS=""
export MOCK_LIST="${task_candidate}"
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 0 "task correction exits 0"
assert_repo_context "${log}" "task correction"
assert_log_has "${log}" "--head fix/foo --base agent/jules" "task correction searches the task branch"
assert_log_has "${log}" "pr ready 7 --repo RamnD/UGS_SDK" "task correction marks the audited pull request ready"
assert_log_lacks "${log}" "--head agent/jules --base agent/jules" "task correction does not search agent/jules to itself"

log="${tmpdir}/integration-fix.log"
: > "${log}"
export CORRECTION_BASE="agent/jules" CORRECTION_NUMBER="12" MERGE_SHA="${SHA_A}"
export MOCK_BODY="$(jules_marker_body integration none "${SHA_A}" 123 clean)"
export MOCK_LIST="${promotion}"
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 0 "integration correction exits 0"
assert_repo_context "${log}" "integration correction"
assert_log_has "${log}" "--head agent/jules --base staging" "integration correction searches the promotion pull request"
assert_log_lacks "${log}" "--head agent/jules --base agent/jules" "integration correction does not search agent/jules to itself"
assert_log_has "${log}" "pr ready 9 --repo RamnD/UGS_SDK" "integration correction marks the promotion draft ready"

log="${tmpdir}/integration-missing.log"
: > "${log}"
export MOCK_LIST="[]"
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "missing promotion target exits 1"
assert_log_lacks "${log}" "pr ready" "missing promotion target does not mark ready"
assert_log_lacks "${log}" "pr create" "missing promotion target does not create a pull request"
assert_repo_context "${log}" "missing promotion"

log="${tmpdir}/protected.log"
: > "${log}"
export CORRECTION_BASE="main" MOCK_LIST="${promotion}"
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "protected correction base exits 1"
if [[ -s "${log}" ]]; then
  fail "protected correction base called gh"
else
  echo "ok: protected correction base does not call gh"
fi

log="${tmpdir}/fix-stale.log"
: > "${log}"
export CORRECTION_BASE="fix/foo" MERGE_SHA="${SHA_A}" MOCK_LIST="${task_stale}"
export MOCK_BODY=""
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "stale correction target exits 1"
assert_log_lacks "${log}" "pr ready" "stale correction target does not mark ready"
assert_repo_context "${log}" "stale correction"

log="${tmpdir}/fix-ambiguous.log"
: > "${log}"
export MOCK_LIST="${two_tasks}" MOCK_BODY=""
set +e
run_fix "${log}"
rc=$?
set -e
assert_eq "${rc}" 1 "ambiguous correction target exits 1"
assert_log_lacks "${log}" "pr ready" "ambiguous correction target does not mark ready"

# Missing GH_REPO fails before any gh command.
set +e
(
  unset GH_REPO
  jules_require_repo
)
rc=$?
set -e
assert_eq "${rc}" 1 "missing GH_REPO is an error"

echo "=== workflow files ==="

python3 - <<'PY'
import pathlib, re, sys, textwrap
import yaml
root = pathlib.Path(".")
lib = (root / ".github/scripts/jules-handoff-lib.sh").read_text()
if not lib.endswith("\n"):
    lib += "\n"
workflows = [
    root / ".github/workflows/jules-audit.yml",
    root / ".github/workflows/jules-fix-ready.yml",
]
for path in workflows:
    text = path.read_text()
    yaml.safe_load(text)
    if "actions/checkout" in text:
        sys.exit(f"{path} checks out a repository")
    if "GH_REPO: ${{ github.repository }}" not in text:
        sys.exit(f"{path} does not set GH_REPO at job scope")
    start = text.index("<<'END_LIB'\n") + len("<<'END_LIB'\n")
    end = text.index("\n          END_LIB\n", start)
    lines = []
    for line in text[start:end].split("\n"):
        if line.startswith("          "):
            lines.append(line[10:])
        else:
            lines.append(line)
    embedded = "\n".join(lines) + "\n"
    if embedded != lib:
        sys.exit(f"{path} embedded library does not match .github/scripts/jules-handoff-lib.sh")
    for lineno, line in enumerate(text.splitlines(), 1):
        if line.strip().startswith("#"):
            continue
        if not re.search(r'(^|[^A-Za-z0-9_])gh\s+(api|pr|label)\b', line):
            continue
        allowed = "--repo" in line or "repos/${GH_REPO}" in line
        if not allowed:
            sys.exit(f"{path}:{lineno} gh call has no repository context: {line.strip()}")
    print(f"ok: {path} yaml, embedded library, and gh repository context")
PY

extract_dir="${tmpdir}/extracted"
mkdir -p "${extract_dir}"
python3 - <<PY
import pathlib
text = pathlib.Path(".github/workflows/jules-audit.yml").read_text()
start = text.index("<<'END_LIB'\n") + len("<<'END_LIB'\n")
end = text.index("\n          END_LIB\n", start)
lines = []
for line in text[start:end].split("\n"):
    lines.append(line[10:] if line.startswith("          ") else line)
pathlib.Path("${extract_dir}/lib.sh").write_text("\n".join(lines) + "\n")
PY
bash -n "${extract_dir}/lib.sh"
echo "ok: embedded library passes bash -n"

# The non-library run blocks are bash too. Extract each run script roughly by
# asking Python for the workflow step run strings and syntax-check them.
python3 - <<PY
import pathlib, subprocess, sys, textwrap, yaml
for path in [".github/workflows/jules-audit.yml", ".github/workflows/jules-fix-ready.yml"]:
    data = yaml.safe_load(pathlib.Path(path).read_text())
    jobs = data["jobs"]
    for job in jobs.values():
        for index, step in enumerate(job.get("steps", []), 1):
            script = step.get("run")
            if not script:
                continue
            proc = subprocess.run(["bash", "-n"], input=script, text=True)
            if proc.returncode != 0:
                sys.exit(f"bash -n failed for {path} step {index} ({step.get('name')})")
print("ok: workflow run blocks pass bash -n")
PY

if (( failures > 0 )); then
  echo "${failures} failure(s)" >&2
  exit 1
fi
echo "All hand-off scenarios passed."
