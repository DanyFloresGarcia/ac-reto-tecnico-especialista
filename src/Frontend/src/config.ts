export const API_EVENT_URL: string = import.meta.env.VITE_API_EVENT_URL ?? "http://localhost:8080";

// Demo-only JWT (Constitucion §5.1 — no login/IdP in this MVP): signed with the
// `JWT_SIGNING_KEY` from your .env. If you change that key, regenerate this token with:
//   dotnet run --project tools/generate-demo-token -- Admin "<tu-jwt-signing-key>"
// and set VITE_ADMIN_TOKEN in .env to the result. See README.md for the full explanation.
export const ADMIN_DEMO_TOKEN: string = import.meta.env.VITE_ADMIN_TOKEN ?? "";
