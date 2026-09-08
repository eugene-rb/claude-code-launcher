"""Claude Code のセッション状態を、ランチャーが読めるマーカーファイルとして書き出すフック。

ランチャー (ClaudeLauncher.App) は音声通知とダッシュボードのバッジのために「いま Claude Code が
何をしているか」を知る必要があるが、次の 3 つはどれもトランスクリプト (~/.claude/projects/**.jsonl)
に書かれない。外から観測できるのはフックだけである。

  permission_prompt  ツールが許可を求めて停止している
  ask_or_plan        AskUserQuestion / ExitPlanMode の確認で停止している
  turn_complete      1 ターンが終わり、次の指示を待っている

出力先は %APPDATA%\\ClaudeLauncher\\status\\<session_id>.json。1 セッション 1 ファイルで、ファイル名
そのものがセッション ID である（ランチャー側は「同じ出来事をもう一度見た」のか「同じセッションの
新しい出来事」なのかを、これと updatedAt で判別する）。

かつては音声通知そのものを ~/.claude/hooks/notify-sound.py が鳴らしていたが、ランチャー側にも同じ
通知があり二重に鳴っていたため廃止した。いまは鳴らすのはランチャーだけで、このフックは状態を
書くだけに徹する（音量設定が 1 か所で効き、Claude Code と Codex CLI が同じ声で通知される）。

~/.claude/settings.json への登録:

  PreToolUse  AskUserQuestion|ExitPlanMode  ... write-status-marker.py" set ask_or_plan
  Notification permission_prompt            ... write-status-marker.py" set permission_prompt
  Stop                                      ... write-status-marker.py" set turn_complete
  PostToolUse                               ... write-status-marker.py" clear
  UserPromptSubmit                          ... write-status-marker.py" clear

clear を PostToolUse と UserPromptSubmit に入れてあるのは、停止していた状態が解消したことを
ランチャーへ伝えるため。マーカーは 10 分で古いと見なされる（Services/StatusMarkerStore.cs）ので、
プロセスが強制終了しても嘘をつき続けることはない。
"""
import datetime
import json
import os
import pathlib
import sys


def status_dir():
    base = os.environ.get("APPDATA") or os.path.expanduser("~")
    return pathlib.Path(base) / "ClaudeLauncher" / "status"


def main():
    try:
        action = sys.argv[1] if len(sys.argv) > 1 else None
        payload = json.loads(sys.stdin.read() or "{}")
        session_id = payload.get("session_id")
        if not session_id:
            return

        d = status_dir()
        d.mkdir(parents=True, exist_ok=True)
        path = d / f"{session_id}.json"

        if action == "clear":
            path.unlink(missing_ok=True)
            return

        reason = sys.argv[2] if len(sys.argv) > 2 else "unknown"
        data = {
            "cwd": payload.get("cwd", ""),
            "reason": reason,
            "updatedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        }
        tmp = path.with_suffix(".tmp")
        tmp.write_text(json.dumps(data), encoding="utf-8")
        tmp.replace(path)
    except Exception:
        # A marker-write failure must never disturb the Claude Code session itself.
        pass


if __name__ == "__main__":
    main()
