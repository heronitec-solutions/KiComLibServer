import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LibraryTableModule } from './library-table.module';

@Component({
    selector: 'library-table-route',
    imports: [LibraryTableModule],
    template: '<library-table></library-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class LibraryTableRouteComponent {}
