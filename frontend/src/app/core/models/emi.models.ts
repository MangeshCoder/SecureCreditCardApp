export interface EmiScheduleItem {
  installmentNumber: number;
  dueDate: string;
  amountDue: number;
  principalComponent: number;
  interestComponent: number;
  paymentStatus: 'Pending' | 'Paid';
  paidDate: string | null;
  isOverdue: boolean;
}

export interface EmiCalculation {
  principalAmount: number;
  tenureMonths: number;
  annualInterestRate: number;
  monthlyInstallment: number;
  totalInterest: number;
  totalRepayable: number;
  schedule: EmiScheduleItem[];
}

export interface EmiOption {
  tenureMonths: number;
  annualInterestRate: number;
  monthlyInstallment: number;
  totalInterest: number;
  totalRepayable: number;
}

export interface EmiRules {
  minimumAmount: number;
  conversionWindowDays: number;
  rates: { tenureMonths: number; annualInterestRate: number }[];
}

export interface EligibleTransaction {
  transactionId: number;
  cardId: number;
  merchantName: string;
  amount: number;
  transactionDate: string;
  convertBeforeUtc: string;
}

export interface EmiPlan {
  emiPlanId: number;
  transactionId: number;
  cardId: number;
  merchantName: string;
  purchaseDate: string;
  principalAmount: number;
  tenureMonths: number;
  annualInterestRate: number;
  monthlyInstallment: number;
  totalInterest: number;
  totalRepayable: number;
  remainingBalance: number;
  outstandingPrincipal: number;
  paidInstallments: number;
  planStatus: 'Active' | 'Closed';
  createdDate: string;
  nextInstallment: EmiScheduleItem | null;
  schedule: EmiScheduleItem[];
}

export interface CardEmiSummary {
  cardId: number;
  activePlans: number;
  outstandingPrincipal: number;
  remainingBalance: number;
  payableOutsideEmi: number;
  nextDueDate: string | null;
  nextDueAmount: number | null;
}
