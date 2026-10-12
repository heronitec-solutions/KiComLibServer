import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

/**
 * Bridges the page header's sidebar trigger to the sidenav owned by
 * `SideMenuComponent`, which is not an ancestor of the routed pages.
 */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  private readonly sidebarToggle = new Subject<void>();

  readonly sidebarToggle$: Observable<void> = this.sidebarToggle.asObservable();

  toggleSidebar(): void {
    this.sidebarToggle.next();
  }
}
