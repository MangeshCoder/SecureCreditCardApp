export type UserRole = 'Cardholder' | 'Admin';

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface UserInfo {
  cardholderId: number;
  fullName: string;
  email: string;
  role: UserRole;
}

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: UserInfo;
}
