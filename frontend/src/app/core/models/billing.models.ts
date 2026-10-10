/** Module 8: billing cycle and statements. */
export type StatementStatus = 'Open' | 'Paid' | 'MinimumPaid' | 'Overdue';

export interface Statement {
  statementId: number;
  cardId: number;
  periodStart: string;
  periodEnd: string;
  dueDate: string;
  openingBalance: number;
  purchases: number;
  cashWithdrawals: number;
  feesAndCharges: number;
  payments: number;
  refunds: number;
  cashback: number;
  movedToEmi: number;
  /** Total amount due (negative = credit balance). */
  closingBalance: number;
  minimumDue: number;
  emiInstallmentsDue: number;
  status: StatementStatus;
  paidByDueDate: number | null;
}

/** Signed like the balance: + increases what you owe, − reduces it. */
export interface StatementLine {
  date: string;
  description: string;
  kind: 'Purchase' | 'Cash' | 'Payment' | 'Refund' | 'Fee' | 'Interest' | 'Tax' | 'Cashback' | 'Emi';
  amount: number;
}

export interface LateFeeSlab {
  upTo: number | null;
  fee: number;
}

export interface BillingRules {
  minimumDuePercent: number;
  minimumDueFloor: number;
  monthlyInterestPercent: number;
  cashAdvanceFeePercent: number;
  cashAdvanceMinimumFee: number;
  gstPercent: number;
  paymentDueDays: number;
  lateFees: LateFeeSlab[];
}

export interface StatementDetail {
  statement: Statement;
  cardholderName: string;
  maskedCardNumber: string;
  creditLimit: number;
  lines: StatementLine[];
  rules: BillingRules;
}

export interface BillingSummary {
  cardId: number;
  lastStatement: Statement | null;
  paidSinceStatement: number;
  remainingMinimumDue: number;
  remainingTotalDue: number;
  isOverdue: boolean;
  unbilledAmount: number;
  nextStatementDate: string;
}
