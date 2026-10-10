import { Component, OnInit, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { BillingRules, BillingSummary, Statement, StatementDetail, StatementStatus } from '../../../core/models/billing.models';
import { Card } from '../../../core/models/card.models';
import { AuthService } from '../../../core/services/auth.service';
import { BillingService } from '../../../core/services/billing.service';
import { CardService } from '../../../core/services/card.service';
import { TransactionService } from '../../../core/services/transaction.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/** Module 8: "your bill", the list of monthly statements with their lines, and PDF download. */
@Component({
  selector: 'app-card-statements',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './card-statements.html'
})
export class CardStatements implements OnInit {
  /** Bound from the route parameter :cardId (withComponentInputBinding). */
  readonly cardId = input.required<string>();

  private readonly billing = inject(BillingService);
  private readonly cards = inject(CardService);
  private readonly transactions = inject(TransactionService);
  protected readonly auth = inject(AuthService);

  protected readonly currency = environment.currencyCode;
  protected readonly card = signal<Card | null>(null);
  protected readonly summary = signal<BillingSummary | null>(null);
  protected readonly statements = signal<Statement[]>([]);
  protected readonly rules = signal<BillingRules | null>(null);
  protected readonly open = signal<StatementDetail | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  ngOnInit(): void {
    this.cards.getCard(this.id).subscribe({ next: c => this.card.set(c), error: e => this.fail(e) });
    this.billing.rules().subscribe({ next: r => this.rules.set(r), error: e => this.fail(e) });
    this.reload();
  }

  /** Admin: close the billing cycle now (normally the scheduler does it every 30 days). */
  generate(): void {
    this.run(this.billing.generate(this.id), s => `Statement generated: total due ${s.closingBalance.toFixed(2)}.`);
  }

  pay(amount: number): void {
    this.run(this.transactions.load(this.id, Number(amount.toFixed(2))), () => `Payment of ${amount.toFixed(2)} received. Thank you!`);
  }

  toggle(s: Statement): void {
    if (this.open()?.statement.statementId === s.statementId) {
      this.open.set(null);
      return;
    }
    this.billing.detail(s.statementId).subscribe({ next: d => this.open.set(d), error: e => this.fail(e) });
  }

  downloadPdf(s: Statement): void {
    this.billing.downloadPdf(s.statementId).subscribe({ error: e => this.fail(e) });
  }

  protected badge(status: StatementStatus): string {
    switch (status) {
      case 'Paid': return 'bg-success';
      case 'MinimumPaid': return 'bg-warning text-dark';
      case 'Overdue': return 'bg-danger';
      default: return 'bg-primary';
    }
  }

  protected statusText(status: StatementStatus): string {
    return status === 'MinimumPaid' ? 'Minimum paid' : status === 'Open' ? 'Open - not due yet' : status;
  }

  /** Calendar days from today to the due date, as the customer counts them (0 = due today). */
  protected daysLeft(dueDate: string): number {
    const day = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()) / 86_400_000;
    return new Date(dueDate).getTime() < Date.now() ? -1 : day(new Date(dueDate)) - day(new Date());
  }

  private reload(): void {
    this.billing.summary(this.id).subscribe({ next: s => this.summary.set(s), error: e => this.fail(e) });
    this.billing.statements(this.id).subscribe({ next: s => this.statements.set(s), error: e => this.fail(e) });
  }

  private run<T>(request: Observable<T>, done: (result: T) => string): void {
    this.busy.set(true);
    this.errors.set([]);
    this.message.set(null);
    request.subscribe({
      next: r => {
        this.message.set(done(r));
        this.busy.set(false);
        this.open.set(null);
        this.reload();
        this.cards.getCard(this.id).subscribe(c => this.card.set(c));
      },
      error: e => {
        this.fail(e);
        this.busy.set(false);
      }
    });
  }

  private fail(e: unknown): void {
    this.errors.set(apiErrorMessages(e));
  }

  private get id(): number {
    return Number(this.cardId());
  }
}
