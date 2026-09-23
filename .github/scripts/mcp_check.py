"""Drives an AzureMCP HTTP endpoint with the same Python MCP client that Open WebUI uses."""

import argparse
import asyncio
import json

from mcp import ClientSession
from mcp.client.streamable_http import streamablehttp_client


async def check(arguments: argparse.Namespace) -> None:
    headers = {"Authorization": f"Bearer {arguments.token}", "X-Azure-DevOps-Pat": arguments.pat}
    async with streamablehttp_client(arguments.url, headers=headers) as (read, write, _):
        async with ClientSession(read, write) as session:
            initialized = await session.initialize()
            assert initialized.instructions, "the server sent no instructions"

            tools = (await session.list_tools()).tools
            assert any(tool.name == "list_projects" for tool in tools), [tool.name for tool in tools]

            result = await session.call_tool(arguments.tool, {})
            if result.structuredContent is not None:
                text = json.dumps(result.structuredContent)
            else:
                text = " ".join(getattr(block, "text", "") for block in result.content)
            print(f"{len(tools)} tools; {arguments.tool} error={bool(result.isError)}: {text[:400]}")

            assert bool(result.isError) == arguments.expect_error, text
            for expected in arguments.expect:
                assert expected in text, f"{expected!r} not in {text!r}"
            for unexpected in arguments.reject:
                assert unexpected not in text, f"{unexpected!r} in {text!r}"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--pat", default="ci-pat")
    parser.add_argument("--tool", default="server_info")
    parser.add_argument("--expect", action="append", default=[], help="text the result must contain")
    parser.add_argument("--reject", action="append", default=[], help="text the result must not contain")
    parser.add_argument("--expect-error", action="store_true", help="the tool call must fail")
    asyncio.run(check(parser.parse_args()))


if __name__ == "__main__":
    main()
