import { Card } from './card.models';

export type TransactionType = 'Swipe' | 'Load' | 'Refund';
export type TransactionStatus = 'Completed' | 'Declined' | 'Refunded';

export interface SwipeRequest {
  cardNumber: string;
  expiryMonth: number;
  expiryYear: number;
  cvv: string;
  pin: string;
  merchantName: string;
  merchantCategoryCode: string;
  amount: number;
}

/** A decline is a normal result (HTTP 200) - check `approved`. */
export interface SwipeResponse {
  approved: boolean;
  status: string;
  declineReason: string | null;
  transactionId: number | null;
  maskedCardNumber: string | null;
  amount: number;
  availableBalance: number | null;
  processedAtUtc: string;
  /** Module 3: cashback credited for an approved swipe (0 if none). */
  cashbackAmount: number;
  cashbackPercentage: number;
}

export interface Transaction {
  transactionId: number;
  cardId: number;
  maskedCardNumber: string;
  merchantName: string;
  merchantCategoryCode: string;
  amount: number;
  transactionType: TransactionType;
  transactionStatus: TransactionStatus;
  declineReason: string | null;
  isEmiConverted: boolean;
  transactionDate: string;
  /** Module 3: cashback earned by this swipe, if any. */
  cashbackEarned: number | null;
  cashbackReversed: boolean;
}

export interface BalanceChangeResponse {
  transaction: Transaction;
  card: Card;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface MerchantCategory {
  code: string;
  description: string;
}
