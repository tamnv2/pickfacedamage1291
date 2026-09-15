# GOOGLE GATEWAY SETUP — PICKFACE DAMAGE 1291

## Scope lock

This setup is valid only for:

- GitHub: `tamnv2/pickfacedamage1291`
- Google Drive root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`
- Google Cloud project: `pickface-damage-1291`

Do not create or reuse resources from any other project/repository/Drive folder.
Do not place OAuth client secrets, refresh tokens, service-account private keys or passwords in this public repository or in the Windows EXE.

## Why the gateway exists

The portable Windows EXE must work on multiple laptops. Firebase Authentication remains the user login. Google Drive/Sheets access is centralized in an Apps Script web app that executes as the OWNER. Every request from the EXE must include a current Firebase ID token; the gateway validates the token and the user's `/users/{uid}` profile before writing.

This means a valid Firebase login can use Google synchronization without a separate Google login on every laptop, while no shared Google credential is embedded in the EXE.

## One-time OWNER deployment

1. Open the approved spreadsheet inside the approved Drive root:
   `Cập nhật thông tin hư hỏng pickface 1291`.
2. Choose **Extensions → Apps Script**. This keeps the script bound to the approved spreadsheet instead of creating a standalone project elsewhere.
3. Name the Apps Script project `Pickface Damage 1291 Gateway`.
4. Replace `Code.gs` with the repository file:
   `apps-script/GoogleGateway/Code.gs`.
5. In Apps Script **Project Settings**, enable showing the manifest file `appsscript.json` in the editor, then replace it with:
   `apps-script/GoogleGateway/appsscript.json`.
6. In **Project Settings → Google Cloud Platform (GCP) Project**, change/link the script to the standard project `pickface-damage-1291` (project number `78092201115`). Do not select any other Cloud project.
7. Choose **Deploy → New deployment → Web app**.
8. Set **Execute as: Me**.
9. Set access to **Anyone** so warehouse laptops do not need a Google account login. Authentication is enforced inside the gateway using the Firebase ID token.
10. Deploy and approve only the requested Drive/Sheets/external-request permissions.
11. Copy the Web App `/exec` URL. The deployment URL is public metadata, not a secret.
12. Put that URL into `runtime-config.json` as `google_gateway_url` in the canonical repository. No EXE rebuild is required; v1.2.2+ loads this public runtime config on login.

## Fixed Google resources

The gateway is hard-coded to these approved resources only:

- Drive root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`
- Images folder: `1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf`
- Damage spreadsheet: `1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4`

The gateway does not scan Drive and does not discover resources by name.

## Expected behavior after deployment

- User signs in with an account created in Firebase Authentication and enabled in Realtime Database `/users/{uid}`.
- The EXE obtains/refreshes its Firebase ID token.
- The EXE automatically calls the gateway; there is no separate Google OAuth dialog on each laptop.
- The gateway validates Firebase identity and permissions, then writes only to the fixed Drive/Sheet IDs above.
- If offline or the gateway is unavailable, the report remains stored locally and retries later.
