import sys, http.server, ssl
from functools import partial
port = int(sys.argv[1])
path = sys.argv[2]
cert = sys.argv[3]
address = ('localhost', port)
handler = partial(http.server.SimpleHTTPRequestHandler, directory=path)
httpd = http.server.HTTPServer(address, handler)
if sys.version_info.major >= 3 and sys.version_info.minor >= 10:
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(cert)
    httpd.socket = context.wrap_socket(httpd.socket, server_side=True)
else:
    httpd.socket = ssl.wrap_socket(httpd.socket, server_side=True, certfile=cert, ssl_version=ssl.PROTOCOL_TLS)
httpd.serve_forever()
