"""Open VibeTerm and ask one Claude panel and two Codex panels a test question.

The panel text log keeps the current process output, not a full-screen picture.
Claude is started with -p, and Codex with exec, so each answer is printed into
its own panel. The script then saves those answers to docs/panel-agent-answers.md.
"""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PROJECT = REPO / "VibeTerm" / "VibeTerm.csproj"
DOCUMENT = REPO / "docs" / "panel-agent-answers.md"
READY_TIMEOUT_SECONDS = 70
ANSWER_TIMEOUT_SECONDS = 240

JOBS = (
    {
        "name": "Claude",
        "slug": "claude",
        "done": "<<<VIBETERM_END_CLAUDE>>>",
        "marker": "CLAUDE_OK",
        "question": (
            "Ответь одним коротким предложением: сколько будет 2 + 2? "
            "В конце ответа напиши слово CLAUDE_OK."
        ),
        "command": (
            "claude -p --restricted --output-format text "
            "(Get-Content -Raw -Encoding utf8 '{question_file}')"
        ),
    },
    {
        "name": "Codex 1",
        "slug": "codex-1",
        "done": "<<<VIBETERM_END_CODEX_1>>>",
        "marker": "CODEX_1_OK",
        "question": (
            "Ответь одним коротким предложением: какая столица Франции? "
            "В конце ответа напиши слово CODEX_1_OK. "
            "Не запускай команды и не меняй файлы."
        ),
        "command": (
            "codex --ask-for-approval never exec --color never --ephemeral "
            "-s read-only --skip-git-repo-check "
            "(Get-Content -Raw -Encoding utf8 '{question_file}')"
        ),
    },
    {
        "name": "Codex 2",
        "slug": "codex-2",
        "done": "<<<VIBETERM_END_CODEX_2>>>",
        "marker": "CODEX_2_OK",
        "question": (
            "Ответь одним коротким предложением: сколько дней в одной неделе? "
            "В конце ответа напиши слово CODEX_2_OK. "
            "Не запускай команды и не меняй файлы."
        ),
        "command": (
            "codex --ask-for-approval never exec --color never --ephemeral "
            "-s read-only --skip-git-repo-check "
            "(Get-Content -Raw -Encoding utf8 '{question_file}')"
        ),
    },
)


class PanelApi:
    def __init__(self, base_url: str, token: str) -> None:
        self.base_url = base_url.rstrip("/")
        self.token = token

    def call(self, method: str, path: str, body: dict | None = None, timeout: float = 40):
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = urllib.request.Request(self.base_url + path, data=data, method=method)
        request.add_header("Authorization", "Bearer " + self.token)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                raw = response.read().decode("utf-8")
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")
            raise RuntimeError(f"{method} {path} вернул {error.code}: {detail}") from error
        return json.loads(raw) if raw else {}

    def panels(self) -> list[dict]:
        return list(self.call("GET", "/api/v1/panels").get("panels", []))

    def create(self, name: str, working_directory: str) -> dict:
        return self.call(
            "POST",
            "/api/v1/panels",
            {"name": name, "workingDirectory": working_directory},
        )

    def close(self, panel_id: int) -> None:
        self.call("DELETE", f"/api/v1/panels/{panel_id}")

    def send(self, panel_id: int, text: str) -> None:
        deadline = time.time() + READY_TIMEOUT_SECONDS
        while True:
            try:
                self.call("POST", f"/api/v1/panels/{panel_id}/input", {"text": text, "submit": True})
                return
            except RuntimeError as error:
                if "409" not in str(error) or time.time() >= deadline:
                    raise
                time.sleep(0.5)

    def read(self, panel_id: int, since: int) -> dict:
        query = urllib.parse.urlencode({"since": since, "maxChars": 100000})
        return self.call("GET", f"/api/v1/panels/{panel_id}/output?{query}")


def find_dotnet() -> Path:
    local = Path(os.environ.get("LOCALAPPDATA", "")) / "Microsoft" / "dotnet" / "dotnet.exe"
    if local.exists():
        return local
    for folder in os.environ.get("PATH", "").split(os.pathsep):
        candidate = Path(folder) / "dotnet.exe"
        if candidate.exists():
            return candidate
    raise FileNotFoundError("Не найден dotnet.exe. Нужен SDK .NET 7.")


def start_app(endpoint_file: Path) -> subprocess.Popen:
    env = os.environ.copy()
    # SpecialFolder.ApplicationData ignores APPDATA and uses the real profile
    # folder. The address file has its own override.
    env["VIBETERM_PANEL_API_FILE"] = str(endpoint_file)
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    dotnet_root = find_dotnet().parent
    env["DOTNET_ROOT"] = str(dotnet_root)
    env["PATH"] = str(dotnet_root) + os.pathsep + env.get("PATH", "")
    return subprocess.Popen(
        [str(find_dotnet()), "run", "--project", str(PROJECT), "--no-build"],
        cwd=REPO,
        env=env,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )


def wait_for_api(endpoint_file: Path, process: subprocess.Popen) -> PanelApi:
    deadline = time.time() + 60
    while time.time() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"VibeTerm завершился сразу, код {process.returncode}.")
        if endpoint_file.exists():
            payload = json.loads(endpoint_file.read_text(encoding="utf-8"))
            api = PanelApi(payload["baseUrl"], payload["token"])
            api.call("GET", "/api/v1/health")
            return api
        time.sleep(0.3)
    raise TimeoutError("Файл адреса панелей не появился.")


def wait_ready(api: PanelApi, panel_id: int) -> dict:
    deadline = time.time() + READY_TIMEOUT_SECONDS
    last = {}
    while time.time() < deadline:
        last = api.call("GET", f"/api/v1/panels/{panel_id}")
        if last.get("ready"):
            return last
        time.sleep(0.4)
    raise TimeoutError(f"Панель {panel_id} не стала готовой: {last.get('status')}.")


def prepare_panels(api: PanelApi) -> list[dict]:
    for panel in api.panels():
        api.close(int(panel["id"]))
    deadline = time.time() + 20
    while api.panels() and time.time() < deadline:
        time.sleep(0.2)

    created = []
    for job in JOBS:
        question_file = Path(tempfile.gettempdir()) / f"vibeterm-{job['slug']}-question.txt"
        question_file.write_text(job["question"].strip() + "\n", encoding="utf-8")
        panel = api.create(job["name"], str(REPO))
        panel = wait_ready(api, int(panel["id"]))
        created.append({**job, "id": int(panel["id"]), "question_file": question_file})
    return created


def launch_questions(api: PanelApi, jobs: list[dict]) -> None:
    for job in jobs:
        script_path = job["question_file"].with_suffix(".ps1")
        question_file = str(job["question_file"])
        script = "\n".join(
            [
                "$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)",
                "chcp 65001 > $null",
                job["command"].format(question_file=question_file),
                f"Write-Output '{job['done']}'",
                "",
            ]
        )
        script_path.write_text(script, encoding="utf-8")
        job["script_file"] = script_path
        before = api.read(job["id"], 0)
        job["since"] = int(before["nextOffset"])
        job["text"] = ""
        api.send(job["id"], f"& '{script_path}'")
        print(f"вопрос отправлен в панель {job['id']} ({job['name']})", flush=True)


def collect_answers(api: PanelApi, jobs: list[dict]) -> None:
    pending = {job["id"]: job for job in jobs}
    deadline = time.time() + ANSWER_TIMEOUT_SECONDS
    while pending and time.time() < deadline:
        for panel_id, job in list(pending.items()):
            chunk = api.read(panel_id, job["since"])
            piece = chunk.get("text") or ""
            if piece:
                job["text"] += piece
                job["since"] = int(chunk["nextOffset"])
            if job["done"] in job["text"]:
                pending.pop(panel_id)
                print(f"ответ получен: {job['name']}", flush=True)
        if pending:
            time.sleep(2)

    for job in jobs:
        job["finished"] = job["done"] in job["text"]
        job["marker_found"] = job["marker"] in job["text"]


def panel_text(job: dict) -> str:
    text = job.get("text") or ""
    done_at = text.find(job["done"])
    if done_at >= 0:
        text = text[:done_at]
    lines = [line.rstrip() for line in text.splitlines()]
    while lines and not lines[-1].strip():
        lines.pop()
    body = "\n".join(lines).strip()
    return body or "(вывод пуст)"


def answer_line(job: dict) -> str:
    found = [line.strip() for line in panel_text(job).splitlines() if job["marker"] in line]
    if found:
        return found[-1]
    return "(маркер ответа в выводе не найден)"


def write_document(jobs: list[dict], started: float) -> None:
    elapsed = int(time.time() - started)
    parts = [
        "# Ответы из трёх панелей",
        "",
        "Проверка интерфейса панелей VibeTerm. В одном окне были открыты три панели: Claude и два Codex.",
        "Каждая программа печатала ответ в свою панель. Ниже сначала короткая строка ответа, затем весь текст панели.",
        "",
        f"Время сбора: {elapsed} с.",
        "",
    ]
    for job in jobs:
        state = "программа завершилась" if job.get("finished") else "ожидание закончилось до завершения"
        marker = "маркер ответа есть" if job.get("marker_found") else "маркера ответа нет"
        parts.extend(
            [
                f"## {job['name']}",
                "",
                f"Панель: {job.get('id', 'не создана')}. {state}. {marker}.",
                "",
                "Вопрос:",
                "",
                job["question"],
                "",
                "Ответ:",
                "",
                answer_line(job),
                "",
                "Текст панели:",
                "",
                "```text",
                panel_text(job),
                "```",
                "",
            ]
        )
    DOCUMENT.parent.mkdir(parents=True, exist_ok=True)
    DOCUMENT.write_text("\n".join(parts), encoding="utf-8")


def stop_app(process: subprocess.Popen) -> None:
    # A normal close saves the three test panels into the user's next session.
    # taskkill skips that save.
    if process.poll() is not None:
        return
    subprocess.run(
        ["taskkill", "/PID", str(process.pid), "/T", "/F"],
        check=False,
        capture_output=True,
        text=True,
    )


def main() -> int:
    started = time.time()
    endpoint_file = Path(tempfile.gettempdir()) / f"vibeterm-panel-api-{os.getpid()}.json"
    print(f"запуск VibeTerm, файл адреса: {endpoint_file}", flush=True)
    process = start_app(endpoint_file)
    jobs: list[dict] = []
    try:
        api = wait_for_api(endpoint_file, process)
        print(f"интерфейс панелей: {api.base_url}", flush=True)
        jobs = prepare_panels(api)
        launch_questions(api, jobs)
        collect_answers(api, jobs)
    finally:
        if jobs:
            write_document(jobs, started)
            print(f"документ: {DOCUMENT}", flush=True)
        else:
            print("панели не были созданы, документ не записан", flush=True)
        stop_app(process)
        endpoint_file.unlink(missing_ok=True)
    failed = [job["name"] for job in jobs if not job.get("finished")]
    return 1 if failed or not jobs else 0


if __name__ == "__main__":
    raise SystemExit(main())
