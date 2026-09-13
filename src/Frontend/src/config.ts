export const API_EVENT_URL: string = import.meta.env.VITE_API_EVENT_URL ?? "http://localhost:8080";

// Demo-only JWT (Constitucion §5.1 — no login/IdP in this MVP): signed with the
// `Jwt__SigningKey` from .env.example. If you change that key, regenerate this token with:
//   dotnet run --project tools/generate-demo-token -- Admin "<tu-jwt-signing-key>"
// and paste the result here. See README.md for the full explanation.
export const ADMIN_DEMO_TOKEN =
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkZW1vLWFkbWluIiwicm9sZSI6IkFkbWluIiwiZXhwIjoxNzg5MzU3Mzk1fQ.XDu8ig6h8z7_PvvxwvAD6deKlVXTvyU7qFmshxnDpq4";
