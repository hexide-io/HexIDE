#!/usr/bin/env python3
"""Run each #1223 probe in a fresh process, including real LSP didOpen notifications.

Build ParseDepthProbe first. Run from the repository root. Logs and generated modules
stay under artifacts/parse-depth; no timing or VB6 acceptance assertions are made.
"""
import json
import os
import pathlib
import queue
import resource
import subprocess
import threading

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/parse-depth'
PROBE = ROOT / 'tools/ParseDepthProbe/bin/Debug/net10.0/ParseDepthProbe.dll'
SERVER = ROOT / 'LspServer/HexIDE.VbLspServer/bin/Debug/net10.0/HexIDE.VbLspServer.dll'


def wire(source, log):
    process = subprocess.Popen(['dotnet', str(SERVER)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=log)
    replies = queue.Queue()

    def read():
        try:
            while True:
                length = None
                while True:
                    line = process.stdout.readline()
                    if not line:
                        raise EOFError('server exited')
                    if line == b'\r\n':
                        break
                    if line.lower().startswith(b'content-length:'):
                        length = int(line.split(b':')[1])
                replies.put(json.loads(process.stdout.read(length)))
        except Exception as error:
            replies.put(error)

    def send(message):
        payload = json.dumps(dict(jsonrpc='2.0', **message)).encode()
        process.stdin.write(f'Content-Length: {len(payload)}\r\n\r\n'.encode() + payload)
        process.stdin.flush()

    def receive(predicate):
        while True:
            reply = replies.get(timeout=10)
            if isinstance(reply, Exception):
                raise reply
            log.write(json.dumps(reply) + '\n')
            log.flush()
            if predicate(reply):
                return reply

    threading.Thread(target=read, daemon=True).start()
    try:
        send(dict(id=1, method='initialize', params=dict(processId=None, rootUri=None, capabilities={})))
        receive(lambda reply: reply.get('id') == 1)
        send(dict(method='initialized', params={}))
        send(dict(method='textDocument/didOpen', params=dict(textDocument=dict(
            uri='file:///probe/Module1.bas', languageId='vb', version=1, text=source))))
        receive(lambda reply: reply.get('method') == 'textDocument/publishDiagnostics')
        # A second request proves that the server still answers after didOpen and its cache walks.
        send(dict(id=2, method='textDocument/documentSymbol', params=dict(
            textDocument=dict(uri='file:///probe/Module1.bas'))))
        receive(lambda reply: reply.get('id') == 2)
        send(dict(id=3, method='shutdown'))
        receive(lambda reply: reply.get('id') == 3)
        send(dict(method='exit'))
        process.stdin.close()
        return process.wait(timeout=10)
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()


def main():
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    OUT.mkdir(parents=True, exist_ok=True)
    os.environ['XDG_DATA_HOME'] = str(OUT / 'user-data')
    rows = []
    for shape in ['chain', 'continuations', 'continued-chain']:
        for depth in [100, 1000, 10000, 100000]:
            source = subprocess.check_output(['dotnet', str(PROBE), 'source', shape, str(depth)], text=True)
            (OUT / f'{shape}-{depth}.bas').write_text(source)
            for host in ['runtime', 'lsp', 'symbols', 'types', 'scope', 'wire']:
                with (OUT / f'{host}-{shape}-{depth}.log').open('w') as log:
                    try:
                        if host == 'wire':
                            result = wire(source, log)
                        else:
                            result = subprocess.run(['dotnet', str(PROBE), host, shape, str(depth)],
                                                    stdout=log, stderr=log, timeout=30).returncode
                    except (subprocess.TimeoutExpired, queue.Empty):
                        result = 'timeout'
                    except Exception as error:
                        result = f'{type(error).__name__}: {error}'
                rows.append([host, shape, depth, result])
                print(rows[-1], flush=True)
                (OUT / 'results.json').write_text(json.dumps(rows, indent=2) + '\n')


if __name__ == '__main__':
    main()
