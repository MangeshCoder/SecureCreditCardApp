import { Component, OnInit, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { CardEmiSummary, EligibleTransaction, EmiOption, EmiPlan } from '../../../core/models/emi.models';
import { EmiService } from '../../../core/services/emi.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/** EMI for one card: summary, purchases that can be converted, and plans with their schedules. */
@Component({
  selector: 'app-card-emi',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './card-emi.html'
})
export class CardEmi implements OnInit {
  /** Bound from the route parameter :cardId. */
  readonly cardId = input.required<string>();

  private readonly emi = inject(EmiService);

  protected readonly currency = environment.currencyCode;
  protected readonly summary = signal<CardEmiSummary | null>(null);
  protected readonly eligible = signal<EligibleTransaction[]>([]);
  protected readonly plans = signal<EmiPlan[]>([]);
  protected readonly errors = signal<string[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** Purchase being converted and the tenure options shown for it. */
  protected readonly converting = signal<EligibleTransaction | null>(null);
  protected readonly options = signal<EmiOption[]>([]);
  protected readonly selectedTenure = signal<number | null>(null);
  /** Plan whose full schedule is expanded. */
  protected readonly expandedPlan = signal<number | null>(null);

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    const id = this.id;
    this.emi.getSummary(id).subscribe({ next: s => this.summary.set(s), error: e => this.errors.set(apiErrorMessages(e)) });
    this.emi.getEligible(id).subscribe({ next: t => this.eligible.set(t), error: e => this.errors.set(apiErrorMessages(e)) });
    this.emi.getPlans(id).subscribe({ next: p => this.plans.set(p), error: e => this.errors.set(apiErrorMessages(e)) });
  }

  startConvert(t: EligibleTransaction): void {
    this.errors.set([]);
    this.message.set(null);
    this.converting.set(t);
    this.selectedTenure.set(null);
    this.emi.getOptions(t.amount).subscribe({
      next: o => {
        this.options.set(o);
        this.selectedTenure.set(o.find(x => x.tenureMonths === 6)?.tenureMonths ?? o[0]?.tenureMonths ?? null);
      },
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  confirmConvert(): void {
    const t = this.converting();
    const tenure = this.selectedTenure();
    if (!t || !tenure) return;
    this.busy.set(true);
    this.emi.convertTransactionToEmi(t.transactionId, tenure).subscribe({
      next: plan => {
        this.busy.set(false);
        this.converting.set(null);
        this.expandedPlan.set(plan.emiPlanId);
        this.message.set(`${t.merchantName} converted to ${plan.tenureMonths} monthly installments of ${plan.monthlyInstallment}.`);
        this.reload();
      },
      error: e => {
        this.busy.set(false);
        this.errors.set(apiErrorMessages(e));
      }
    });
  }

  pay(plan: EmiPlan): void {
    const next = plan.nextInstallment;
    if (!next || this.busy()) return;
    this.busy.set(true);
    this.errors.set([]);
    this.emi.payInstallment(plan.emiPlanId, next.installmentNumber).subscribe({
      next: r => {
        this.busy.set(false);
        this.message.set(r.plan.planStatus === 'Closed'
          ? `Last installment paid – the ${plan.merchantName} EMI plan is closed.`
          : `Installment ${next.installmentNumber} of ${plan.tenureMonths} paid.`);
        this.reload();
      },
      error: e => {
        this.busy.set(false);
        this.errors.set(apiErrorMessages(e));
      }
    });
  }

  toggle(planId: number): void {
    this.expandedPlan.update(current => (current === planId ? null : planId));
  }

  selectedOption(): EmiOption | undefined {
    return this.options().find(o => o.tenureMonths === this.selectedTenure());
  }

  private get id(): number {
    return Number(this.cardId());
  }
}
