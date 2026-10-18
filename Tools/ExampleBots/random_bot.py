# A Mandate seat in about 25 lines of Python: the "bring your own bot" wire format end to end.
#
#   python random_bot.py 8765
#
# then seat it with kind "http" and endpoint http://localhost:8765/ (Unity inspector, or seats.json:
#   { "kind": "http", "endpoint": "http://localhost:8765/", "name": "Python bot" }
#
# Each phase the referee POSTs the view as JSON (see Docs/Agents.md for every field) and expects a JSON reply.
# Anything missing or malformed simply counts as a pass, exactly as for a language model that fails to answer.

import json, random, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

class Bot(BaseHTTPRequestHandler):
    def do_POST(self):
        view = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        phase, reply = view["phase"], {"notes": "python bot, round %d" % view["round"]}
        if phase == "draft":
            reply["draft"] = view["draftOptions"][0]["id"]
        elif phase == "negotiate":
            reply["messages"] = [{"text": "beep boop, I come in peace"}, {"to": view["left"], "text": "want to trade later?"}]
        elif phase == "act":
            reply["action"] = random.choice(view["legalActions"])
        elif phase == "guess":
            ids = [c["id"] for c in view["guessCandidates"]]
            reply["guesses"] = {p: random.choice(ids) for p in view["players"] if p != view["you"]}
        body = json.dumps(reply).encode()
        self.send_response(200)
        self.cors()
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    # A web (WebGL) build of the game runs in a browser, which first asks the bot whether a web page may call it.
    def do_OPTIONS(self):
        self.send_response(204)
        self.cors()
        self.end_headers()

    def cors(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization")
        self.send_header("Access-Control-Allow-Methods", "POST, OPTIONS")

    def log_message(self, *args):
        pass

port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
print("Mandate python bot listening on http://localhost:%d/" % port)
HTTPServer(("0.0.0.0", port), Bot).serve_forever()
