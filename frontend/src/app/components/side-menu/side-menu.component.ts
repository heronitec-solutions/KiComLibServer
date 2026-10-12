import { Component, OnDestroy, OnInit, ViewChild, ChangeDetectionStrategy } from '@angular/core';

import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Subscription } from 'rxjs';
import { ContentAreaComponent } from '../content-area/content-area.component';
import { LayoutService } from '../../services/layout.service';
import { AuthService } from '../../services/auth.service';
import { NAV_GROUPS, NavGroup, NavItem } from './nav-items';

@Component({
    selector: 'side-menu',
    imports: [
    ContentAreaComponent,
    MatSidenavModule,
    MatIconModule,
    RouterLink,
    RouterLinkActive
],
    templateUrl: './side-menu.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './side-menu.component.scss'
})
export class SideMenuComponent implements OnInit, OnDestroy {
  readonly navGroups: NavGroup[] = NAV_GROUPS;

  @ViewChild('drawer') drawer?: MatSidenav;

  private toggleSubscription?: Subscription;

  constructor(
    private layoutService: LayoutService,
    public auth: AuthService
  ) { }

  ngOnInit(): void {
    this.toggleSubscription = this.layoutService.sidebarToggle$.subscribe(() => {
      this.drawer?.toggle();
    });
  }

  ngOnDestroy(): void {
    this.toggleSubscription?.unsubscribe();
  }

  visibleGroups(): NavGroup[] {
    return this.navGroups.filter(group => this.visibleItems(group).length > 0);
  }

  visibleItems(group: NavGroup): NavItem[] {
    return group.items.filter(item => {
      if (item.adminOnly && !this.auth.profile().isAdmin) {
        return false;
      }
      if (item.requiresWarehouse && !this.auth.canReadWarehouse()) {
        return false;
      }
      return true;
    });
  }
}
