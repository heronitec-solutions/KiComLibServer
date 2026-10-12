import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LibraryFileTableModule } from './library-file-table.module';

@Component({
    selector: 'library-file-table-route',
    imports: [LibraryFileTableModule],
    template: '<library-file-table></library-file-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class LibraryFileTableRouteComponent {}
