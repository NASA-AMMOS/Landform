import sys, http.server, ssl
from functools import partial
port = int(sys.argv[1])
path = sys.argv[2]
cert = sys.argv[3]
address = ('localhost', port)
handler = partial(http.server.SimpleHTTPRequestHandler, directory=path)
httpd = http.server.HTTPServer(address, handler)
httpd.socket = ssl.wrap_socket(httpd.socket, server_side=True, certfile=cert, ssl_version=ssl.PROTOCOL_TLS)
httpd.serve_forever()
