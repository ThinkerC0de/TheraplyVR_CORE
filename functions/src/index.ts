import * as admin from "firebase-admin";

admin.initializeApp();

export { reconcileSession } from "./reconcile";
// sessionIngest removed — mobile controller is now the sole Firestore writer
