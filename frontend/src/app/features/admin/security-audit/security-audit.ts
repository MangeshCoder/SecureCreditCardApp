import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { AuditLog } from '../../../core/models/audit.models';
import { PagedResult } from '../../../core/models/transaction.models';
import { AuditService } from '../../../core/services/audit.service';
import { apiErrorMessages } from '../../../core/utils/api-error';

/** Security audit trail: every gateway call and sensitive user action, with filters. */
@Component({
  selector: 'app-security-audit',
  imports: [DatePipe, ReactiveFormsModule],
  templateUrl: './security-audit.html'
})
export class SecurityAudit implements OnInit {
  private readonly audit = inject(AuditService);

  protected readonly actionTypes = signal<string[]>([]);
  protected readonly logs = signal<PagedResult<AuditLog> | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly expanded = signal<number | null>(null);

  protected readonly filters = inject(FormBuilder).nonNullable.group({
    actionType: [''],
    outcome: [''],
    partnerId: [''],
    lastHours: [24]
  });

  ngOnInit(): void {
    this.audit.getActionTypes().subscribe({
      next: t => this.actionTypes.set(t),
      error: e => this.errors.set(apiErrorMessages(e))
    });
    this.load(1);
  }

  load(page: number): void {
    const f = this.filters.getRawValue();
    this.errors.set([]);
    this.audit.getLogs({
      actionType: f.actionType, outcome: f.outcome, partnerId: f.partnerId,
      lastHours: Number(f.lastHours) || null, page, pageSize: 25
    }).subscribe({
      next: l => this.logs.set(l),
      error: e => this.errors.set(apiErrorMessages(e))
    });
  }

  quickFilter(outcome: string): void {
    this.filters.patchValue({ outcome });
    this.load(1);
  }

  toggle(id: number): void {
    this.expanded.update(current => (current === id ? null : id));
  }

  badge(outcome: string): string {
    return outcome === 'Success' ? 'bg-success' : outcome === 'Rejected' ? 'bg-danger' : 'bg-warning text-dark';
  }
}