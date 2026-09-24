"""Calls the generated Open WebUI plugin the way Open WebUI does, against a running AzureMCP server."""

import argparse
import asyncio
import hashlib
import importlib.util


def load(path: str):
    spec = importlib.util.spec_from_file_location("azure_devops_plugin", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def fingerprint(pat: str) -> str:
    return hashlib.sha256(pat.encode()).hexdigest()[:8]


async def check(arguments: argparse.Namespace) -> None:
    plugin = load(arguments.plugin)
    tools = plugin.Tools()
    tools.valves.server_url = arguments.url
    tools.valves.server_token = arguments.token

    def user(pat: str) -> dict:
        return {"id": f"user-{fingerprint(pat)}", "valves": tools.UserValves(ado_pat=pat)}

    async def list_projects(pat: str) -> tuple[str, str]:
        return pat, await tools.list_projects(__user__=user(pat))

    # Several users at once, interleaved, through one plugin instance: each must reach Azure DevOps as themselves.
    pats = [f"ci-user-{number}-pat" for number in range(4)]
    results = await asyncio.gather(*(list_projects(pat) for pat in pats * 5))
    for pat, answer in results:
        assert f"seen with PAT {fingerprint(pat)}" in answer, (pat, answer)
    print(f"{len(results)} concurrent calls from {len(pats)} users each reached Azure DevOps with their own PAT")

    answer = await tools.list_projects(__user__={"id": "nobody", "valves": tools.UserValves()})
    assert "PAT is not set" in answer, answer

    tools.valves.server_token = "not-the-token"
    answer = await tools.list_projects(__user__=user(pats[0]))
    # A model relays this to the user, so it must say whose token is wrong and that their PAT is not.
    for phrase in ("rejected the plugin's token", "not about your PAT", "server_token", "on the AzureMCP server"):
        assert phrase in answer, (phrase, answer)
    print("A missing PAT and a wrong token are both explained to the user")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--plugin", required=True)
    parser.add_argument("--url", required=True)
    parser.add_argument("--token", required=True)
    asyncio.run(check(parser.parse_args()))


if __name__ == "__main__":
    main()
