export type CardStatus = 'Active' | 'Blocked';

export interface Card {
  cardId: number;
  cardholderId: number;
  maskedCardNumber: string;
  creditLimit: number;
  availableBalance: number;
  outstandingAmount: number;
  cardStatus: CardStatus;
  expiryDate: string;
  createdAt: string;
}

export interface IssueCardRequest {
  cardholderId: number;
  creditLimit: number;
}

/** Returned once at issuance - the only time the full number, CVV and initial PIN are visible. */
export interface IssuedCardResponse {
  card: Card;
  cardNumber: string;
  cvv: string;
  initialPin: string;
}

export interface ChangePinRequest {
  currentPin: string;
  newPin: string;
}

export interface RevealCardNumberResponse {
  cardId: number;
  cardNumber: string;
}

export interface Cardholder {
  cardholderId: number;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  role: string;
  isActive: boolean;
  createdAt: string;
  cardCount: number;
}
