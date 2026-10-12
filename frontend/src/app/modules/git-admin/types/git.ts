export type GitHostKind = 'gitlab' | 'github' | 'generic';
export type GitCredentialKind = 'https_token' | 'ssh_key';
export type GitSyncMode = 'webhook' | 'poll';
export type GitRepositoryStatus = 'new' | 'ok' | 'error' | 'disabled';

export interface GitHost {
  id: number;
  name: string;
  kind: GitHostKind;
  webUrl: string;
  sshHost: string;
  caCertificate: string;
  allowHttp: boolean;
  isSystem: boolean;
  sshKnownHosts: string;
}

export interface GitHostWrite {
  kind: GitHostKind;
  name: string;
  webUrl: string;
  sshHost: string;
  caCertificate: string;
  allowHttp: boolean;
}

export interface SshHostKeyScan {
  fingerprints: string[];
  knownHosts: string;
}

export interface GitCredential {
  id: number;
  hostId: number;
  name: string;
  kind: GitCredentialKind;
  username: string;
  secretHint: string;
  publicKey: string;
  expiresAt: string;
  lastError: string;
}

export interface GitCredentialWrite {
  hostId: number;
  name: string;
  kind: GitCredentialKind;
  username: string;
  secret: string;
  generate: boolean;
  expiresAt: string;
}

export interface GitRepository {
  id: number;
  hostId: number;
  credentialId: number | null;
  cloneUrl: string;
  webUrl: string;
  providerPath: string;
  defaultBranch: string;
  trackedBranches: string[];
  tagPattern: string;
  syncMode: GitSyncMode;
  status: GitRepositoryStatus;
  lastFetchAt: string;
  lastError: string;
}

export interface GitWebhookInfo {
  url: string;
  secret: string;
  instructions: string;
}

export interface GitRepositoryCreated extends GitRepository {
  webhook: GitWebhookInfo | null;
}

export interface GitTestResult {
  ok: boolean;
  defaultBranch: string;
  branches: string[];
  tags: string[];
  error: string;
}

export interface DiscoveredProject {
  projectFile: string;
  name: string;
  alreadyProject: boolean;
  projectId: number | null;
}
