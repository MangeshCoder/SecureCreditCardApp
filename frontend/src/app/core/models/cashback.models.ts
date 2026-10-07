export interface CashbackRule {
  merchantCategoryCode: string;
  description: string;
  percentage: number;
}

export interface CashbackRules {
  categoryRules: CashbackRule[];
  defaultPercentage: number;
  minimumSpend: number;
  maxCashbackPerTransaction: number;
}

export interface CashbackCategoryTotal {
  merchantCategoryCode: string;
  description: string;
  amount: number;
  transactionCount: number;
}

export interface CashbackSummary {
  cardId: number;
  maskedCardNumber: string;
  totalEarned: number;
  totalReversed: number;
  netCashback: number;
  thisMonth: number;
  byCategory: CashbackCategoryTotal[];
}

export type CashbackType = 'Earned' | 'Reversed';

export interface CashbackLog {
  cashbackId: number;
  transactionId: number;
  cardId: number;
  merchantName: string;
  merchantCategoryCode: string;
  transactionAmount: number;
  cashbackPercentage: number;
  cashbackAmount: number;
  cashbackType: CashbackType;
  creditedDate: string;
}
