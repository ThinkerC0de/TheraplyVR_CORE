#!/usr/bin/env node

"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith("--")) {
      continue;
    }

    const key = token.substring(2);
    const next = argv[i + 1];
    if (!next || next.startsWith("--")) {
      args[key] = true;
      continue;
    }

    args[key] = next;
    i += 1;
  }

  return args;
}

function fail(message) {
  throw new Error(message);
}

function readJson(filePath, label) {
  let raw;
  try {
    raw = fs.readFileSync(filePath, "utf8");
  } catch (error) {
    fail(`Cannot read ${label} file: ${filePath}\n${error.message}`);
  }

  try {
    return JSON.parse(raw.replace(/^\uFEFF/, ""));
  } catch (error) {
    fail(`Cannot parse ${label} JSON: ${filePath}\n${error.message}`);
  }
}

function toFirestoreValue(value) {
  if (value === null) {
    return { nullValue: null };
  }

  if (Array.isArray(value)) {
    return {
      arrayValue: {
        values: value.map((item) => toFirestoreValue(item)),
      },
    };
  }

  const type = typeof value;
  if (type === "string") {
    return { stringValue: value };
  }

  if (type === "boolean") {
    return { booleanValue: value };
  }

  if (type === "number") {
    if (!Number.isFinite(value)) {
      throw new Error(`Non-finite number cannot be serialized: ${value}`);
    }

    if (Number.isInteger(value)) {
      return { integerValue: value.toString() };
    }

    return { doubleValue: value };
  }

  if (type === "object") {
    const fields = {};
    for (const [key, nestedValue] of Object.entries(value)) {
      if (nestedValue === undefined) {
        continue;
      }
      fields[key] = toFirestoreValue(nestedValue);
    }

    return {
      mapValue: {
        fields,
      },
    };
  }

  throw new Error(`Unsupported JSON value type: ${type}`);
}

function toFirestoreFields(objectValue) {
  if (objectValue === null || typeof objectValue !== "object" || Array.isArray(objectValue)) {
    throw new Error("Catalog entry must be a JSON object.");
  }

  const root = toFirestoreValue(objectValue);
  const fields = root && root.mapValue ? root.mapValue.fields : null;
  if (!fields || typeof fields !== "object") {
    throw new Error("Could not convert catalog entry to Firestore fields.");
  }

  return fields;
}

function resolveFirebaseCliExecutable() {
  return process.platform === "win32" ? "firebase.cmd" : "firebase";
}

function runFirebaseLoginList() {
  if (process.platform === "win32") {
    return spawnSync("cmd.exe", ["/d", "/s", "/c", "firebase.cmd login:list --json"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });
  }

  return spawnSync("firebase", ["login:list", "--json"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true,
  });
}

const FIREBASE_CLI_CLIENT_ID =
  process.env.FIREBASE_CLIENT_ID || "563584335869-fgrhgmd47bqnekij5i8b5pr03ho849e6.apps.googleusercontent.com";
const FIREBASE_CLI_CLIENT_SECRET = process.env.FIREBASE_CLIENT_SECRET || "j9iVZfS8kkCEFUPaAeJV0sAi";
const FIREBASE_TOKEN_EXCHANGE_URL = "https://www.googleapis.com/oauth2/v3/token";
const FIREBASE_TOKEN_SCOPES = [
  "https://www.googleapis.com/auth/cloud-platform",
  "https://www.googleapis.com/auth/firebase",
];

function parseFirebaseLoginListPayload(rawPayload) {
  let payload;
  try {
    payload = JSON.parse(rawPayload || "{}");
  } catch (error) {
    fail(`Cannot parse Firebase CLI login:list output.\n${error.message}`);
  }

  const results = Array.isArray(payload.result) ? payload.result : [];
  for (const entry of results) {
    const tokens = entry && typeof entry.tokens === "object" ? entry.tokens : null;
    if (!tokens) {
      continue;
    }

    const refreshToken = typeof tokens.refresh_token === "string" ? tokens.refresh_token.trim() : "";
    const accessToken = typeof tokens.access_token === "string" ? tokens.access_token.trim() : "";
    if (refreshToken || accessToken) {
      return {
        refreshToken,
        accessToken,
      };
    }
  }

  fail("No Firebase CLI tokens found. Run `firebase login` first.");
}

async function exchangeRefreshTokenForAccessToken(refreshToken) {
  const trimmed = typeof refreshToken === "string" ? refreshToken.trim() : "";
  if (!trimmed) {
    fail("Refresh token is empty.");
  }

  const payload = new URLSearchParams({
    refresh_token: trimmed,
    client_id: FIREBASE_CLI_CLIENT_ID,
    client_secret: FIREBASE_CLI_CLIENT_SECRET,
    grant_type: "refresh_token",
    scope: FIREBASE_TOKEN_SCOPES.join(" "),
  });

  const response = await fetch(FIREBASE_TOKEN_EXCHANGE_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/x-www-form-urlencoded",
    },
    body: payload.toString(),
  });

  const raw = await response.text();
  let parsed = null;
  if (raw && raw.trim().length > 0) {
    try {
      parsed = JSON.parse(raw);
    } catch (_) {
      parsed = null;
    }
  }

  if (!response.ok) {
    const details = parsed ? JSON.stringify(parsed) : raw;
    fail(
      `Failed to refresh Firebase access token (HTTP ${response.status} ${response.statusText}). ${details}`
    );
  }

  const accessToken = parsed && typeof parsed.access_token === "string" ? parsed.access_token.trim() : "";
  if (!accessToken) {
    fail("OAuth token exchange succeeded but returned empty access_token.");
  }

  return accessToken;
}

async function resolveAccessToken(explicitToken) {
  if (explicitToken && explicitToken.trim().length > 0) {
    const trimmedExplicit = explicitToken.trim();
    if (trimmedExplicit.startsWith("1//")) {
      return exchangeRefreshTokenForAccessToken(trimmedExplicit);
    }
    return trimmedExplicit;
  }

  const tokenFromEnv = process.env.FIREBASE_ACCESS_TOKEN;
  if (tokenFromEnv && tokenFromEnv.trim().length > 0) {
    const trimmedAccess = tokenFromEnv.trim();
    if (trimmedAccess.startsWith("1//")) {
      return exchangeRefreshTokenForAccessToken(trimmedAccess);
    }
    return trimmedAccess;
  }

  const refreshTokenFromEnv = process.env.FIREBASE_TOKEN;
  if (refreshTokenFromEnv && refreshTokenFromEnv.trim().length > 0) {
    return exchangeRefreshTokenForAccessToken(refreshTokenFromEnv.trim());
  }

  const firebaseCli = resolveFirebaseCliExecutable();
  const loginList = runFirebaseLoginList();

  if (loginList.error) {
    fail(
      `Cannot execute Firebase CLI (${firebaseCli}). Install CLI or pass --access-token.\n${loginList.error.message}`
    );
  }

  if (loginList.status !== 0) {
    const stderr = (loginList.stderr || "").trim();
    fail(
      `Firebase CLI login:list failed with code ${loginList.status}. ${stderr || "Run `firebase login` first."}`
    );
  }

  const cliTokens = parseFirebaseLoginListPayload(loginList.stdout || "{}");
  if (cliTokens.refreshToken) {
    return exchangeRefreshTokenForAccessToken(cliTokens.refreshToken);
  }

  if (cliTokens.accessToken) {
    return cliTokens.accessToken;
  }

  fail("No Firebase CLI access token found. Run `firebase login` or pass --access-token.");
}

async function requestJson(url, options) {
  const response = await fetch(url, options);
  const raw = await response.text();
  let parsed = null;
  if (raw && raw.trim().length > 0) {
    try {
      parsed = JSON.parse(raw);
    } catch (_) {
      parsed = null;
    }
  }

  if (!response.ok) {
    const details = parsed ? JSON.stringify(parsed) : raw;
    throw new Error(`HTTP ${response.status} ${response.statusText} :: ${details}`);
  }

  return parsed;
}

async function deleteDocument(documentUrl, authHeader) {
  const response = await fetch(documentUrl, {
    method: "DELETE",
    headers: authHeader,
  });

  if (response.status === 404) {
    return false;
  }

  if (!response.ok) {
    const raw = await response.text();
    throw new Error(`DELETE failed ${response.status} ${response.statusText}: ${raw}`);
  }

  return true;
}

async function upsertDocument(documentUrl, authHeader, entry) {
  const fields = toFirestoreFields(entry);
  await requestJson(documentUrl, {
    method: "PATCH",
    headers: {
      ...authHeader,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ fields }),
  });
}

async function listCollectionDocumentIds(collectionUrl, authHeader) {
  const ids = [];
  let pageToken = "";

  while (true) {
    const url = new URL(collectionUrl);
    url.searchParams.set("pageSize", "300");
    if (pageToken) {
      url.searchParams.set("pageToken", pageToken);
    }

    const payload = await requestJson(url.toString(), {
      method: "GET",
      headers: authHeader,
    });

    const documents = Array.isArray(payload && payload.documents) ? payload.documents : [];
    for (const doc of documents) {
      const fullName = typeof doc.name === "string" ? doc.name : "";
      if (!fullName) {
        continue;
      }

      const slashIndex = fullName.lastIndexOf("/");
      if (slashIndex <= 0 || slashIndex >= fullName.length - 1) {
        continue;
      }

      ids.push(fullName.substring(slashIndex + 1));
    }

    pageToken = payload && typeof payload.nextPageToken === "string" ? payload.nextPageToken : "";
    if (!pageToken) {
      break;
    }
  }

  return ids;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const scriptDir = __dirname;
  const defaultRepoRoot = path.resolve(scriptDir, "..");
  const repoRoot = path.resolve(args["repo-root"] || defaultRepoRoot);
  const catalogPath = path.resolve(args["catalog-path"] || path.join(repoRoot, "contracts", "game_catalog_seed.json"));
  const dryRun = Boolean(args["dry-run"]);
  const deleteMissing = Boolean(args["delete-missing"]);
  const explicitToken = typeof args["access-token"] === "string" ? args["access-token"] : "";

  const catalogRoot = readJson(catalogPath, "catalog");
  const entries = Array.isArray(catalogRoot.entries) ? catalogRoot.entries : [];
  if (entries.length === 0) {
    fail(`Catalog entries are empty: ${catalogPath}`);
  }

  const collection = typeof catalogRoot.collection === "string" && catalogRoot.collection.trim().length > 0
    ? catalogRoot.collection.trim()
    : "game_catalog";
  if (collection !== "game_catalog") {
    fail(`Unsupported collection '${collection}'. Expected 'game_catalog'.`);
  }

  const projectId = (
    args["project-id"] ||
    process.env.FIREBASE_PROJECT_ID ||
    "theraply-vr-demo"
  ).toString().trim();
  if (!projectId) {
    fail("Missing Firebase project id. Pass --project-id or set FIREBASE_PROJECT_ID.");
  }

  const uniqueGameIds = new Set();
  for (const entry of entries) {
    const gameId = entry && typeof entry.gameId === "string" ? entry.gameId.trim() : "";
    if (!gameId) {
      fail("Each catalog entry must define non-empty gameId.");
    }

    if (uniqueGameIds.has(gameId)) {
      fail(`Duplicate gameId in catalog: ${gameId}`);
    }

    uniqueGameIds.add(gameId);
  }

  console.log(`[SYNC] Repo root: ${repoRoot}`);
  console.log(`[SYNC] Catalog path: ${catalogPath}`);
  console.log(`[SYNC] Firebase project: ${projectId}`);
  console.log(`[SYNC] Collection: ${collection}`);
  console.log(`[SYNC] Entries: ${entries.length}`);
  if (dryRun) {
    console.log("[SYNC] Mode: DRY-RUN (no remote writes)");
  }

  if (dryRun) {
    for (const entry of entries) {
      console.log(`[DRY] upsert ${entry.gameId}`);
    }
    if (deleteMissing) {
      console.log("[DRY] delete-missing requested (skipped network listing in dry-run).");
    }
    console.log("[DONE] Dry-run completed.");
    return;
  }

  const token = await resolveAccessToken(explicitToken);
  const authHeader = {
    Authorization: `Bearer ${token}`,
  };
  const collectionUrl = `https://firestore.googleapis.com/v1/projects/${encodeURIComponent(projectId)}/databases/(default)/documents/${collection}`;

  let deletedMissing = 0;
  if (deleteMissing) {
    const existingIds = await listCollectionDocumentIds(collectionUrl, authHeader);
    const staleIds = existingIds.filter((id) => !uniqueGameIds.has(id));
    for (const staleId of staleIds) {
      const staleUrl = `${collectionUrl}/${encodeURIComponent(staleId)}`;
      await deleteDocument(staleUrl, authHeader);
      deletedMissing += 1;
      console.log(`[DELETE] removed stale doc ${staleId}`);
    }
  }

  let replaced = 0;
  for (const entry of entries) {
    const gameId = entry.gameId.trim();
    const documentUrl = `${collectionUrl}/${encodeURIComponent(gameId)}`;
    await deleteDocument(documentUrl, authHeader);
    await upsertDocument(documentUrl, authHeader, entry);
    replaced += 1;
    console.log(`[UPSERT] ${gameId}`);
  }

  console.log(`[DONE] Synced game_catalog to Firestore.`);
  console.log(`[DONE] Replaced docs: ${replaced}`);
  if (deleteMissing) {
    console.log(`[DONE] Deleted stale docs: ${deletedMissing}`);
  }
}

main().catch((error) => {
  const message = error && error.message ? error.message : String(error);
  console.error(`[FAIL] ${message}`);
  process.exitCode = 1;
});
