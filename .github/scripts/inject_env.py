"""Write production settings into the published web.config as IIS environment variables.

Values come from GitHub Actions secrets/variables (environment of this step), never from the repo.
Empty values are skipped, so optional settings (e.g. Razorpay) can simply be left unset.
Usage: python inject_env.py publish/web.config
"""
import os
import sys
import xml.etree.ElementTree as ET

# environment variable name for ASP.NET Core  ->  name of the variable set by the workflow step
MAP = {
    "ASPNETCORE_ENVIRONMENT": "ASPNETCORE_ENVIRONMENT",
    "ConnectionStrings__DefaultConnection": "DB_CONNECTION",
    "Jwt__SecretKey": "JWT_SECRET",
    "Security__DataEncryptionKey": "DATA_ENCRYPTION_KEY",
    "Security__DataEncryptionKeyId": "DATA_ENCRYPTION_KEY_ID",
    "Email__Password": "EMAIL_PASSWORD",
    "App__FrontendUrl": "FRONTEND_URL",
    "App__AssetBaseUrl": "ASSET_BASE_URL",
    "Razorpay__KeyId": "RAZORPAY_KEY_ID",
    "Razorpay__KeySecret": "RAZORPAY_KEY_SECRET",
}
REQUIRED = ["DB_CONNECTION", "JWT_SECRET", "DATA_ENCRYPTION_KEY"]

missing = [k for k in REQUIRED if not os.environ.get(k, "").strip()]
if missing:
    sys.exit(f"Missing required GitHub secrets: {', '.join(missing)}")

path = sys.argv[1]
tree = ET.parse(path)
core = tree.getroot().find(".//aspNetCore")
if core is None:
    sys.exit("aspNetCore element not found in web.config")

env = core.find("environmentVariables")
if env is None:
    env = ET.SubElement(core, "environmentVariables")
for child in list(env):
    env.remove(child)

count = 0
for name, var in MAP.items():
    value = os.environ.get(var, "").strip()
    if value:
        ET.SubElement(env, "environmentVariable", name=name, value=value)
        count += 1

tree.write(path, encoding="utf-8", xml_declaration=True)
print(f"web.config: {count} environment variables written")  # names only, never values
